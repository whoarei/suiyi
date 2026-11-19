# Builds and signs the sideload APK using the Windows user's Suiyi signing key.
[CmdletBinding()]
param([switch]$InitializeLocalKey)

$ErrorActionPreference = 'Stop'
if (-not $env:JAVA_HOME) { throw 'Set JAVA_HOME to a JDK 17 or 21 installation.' }
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
if (-not $sdk) { throw 'Set ANDROID_HOME to the Android SDK directory.' }
$keytool = Join-Path $env:JAVA_HOME 'bin/keytool.exe'
$signer = Join-Path $sdk 'build-tools/36.0.0/apksigner.bat'
$signingDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Suiyi/AndroidSigning'
$keystore = Join-Path $signingDir 'suiyi-release.p12'
$secret = Join-Path $signingDir 'password.dpapi'

if ((Test-Path -LiteralPath $keystore) -ne (Test-Path -LiteralPath $secret)) {
    throw "The signing key files in $signingDir are incomplete. Restore both files before building."
}

try {
    if (-not (Test-Path -LiteralPath $keystore)) {
        if (-not $InitializeLocalKey) {
            throw 'A Suiyi signing key is missing. Run once with -InitializeLocalKey to create it on this computer.'
        }
        New-Item -ItemType Directory -Force -Path $signingDir | Out-Null
        $random = [byte[]]::new(32)
        $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $generator.GetBytes($random) } finally { $generator.Dispose() }
        $env:SUIYI_LOCAL_ANDROID_PASSWORD = [Convert]::ToBase64String($random)
        $encrypted = ConvertTo-SecureString $env:SUIYI_LOCAL_ANDROID_PASSWORD -AsPlainText -Force |
            ConvertFrom-SecureString
        & $keytool -genkeypair -keystore $keystore -storetype PKCS12 -alias suiyi-android `
            '-storepass:env' SUIYI_LOCAL_ANDROID_PASSWORD -keyalg RSA -keysize 3072 `
            -validity 10000 -dname 'CN=Suiyi Android'
        if ($LASTEXITCODE) { throw "keytool failed ($LASTEXITCODE)." }
        Set-Content -LiteralPath $secret -Value $encrypted
        Remove-Item Env:SUIYI_LOCAL_ANDROID_PASSWORD
    }

    Push-Location $PSScriptRoot
    try {
        & './gradlew.bat' --console=plain ':app:assembleRelease'
        if ($LASTEXITCODE) { throw "Gradle failed ($LASTEXITCODE)." }
    } finally {
        Pop-Location
    }

    $metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'app/build/outputs/apk/release/output-metadata.json') -Raw |
        ConvertFrom-Json
    $output = $metadata.elements[0]
    $source = Join-Path $PSScriptRoot "app/build/outputs/apk/release/$($output.outputFile)"
    $artifactDir = Join-Path $PSScriptRoot '../artifacts/android'
    New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
    $fileName = "suiyi-$($output.versionName)-android.apk"
    $apk = Join-Path $artifactDir $fileName
    $securePassword = ConvertTo-SecureString (Get-Content -LiteralPath $secret -Raw).Trim()
    $credential = [pscredential]::new('suiyi-android', $securePassword)
    $env:SUIYI_LOCAL_ANDROID_PASSWORD = $credential.GetNetworkCredential().Password
    & $signer sign --ks $keystore --ks-key-alias suiyi-android `
        --ks-pass 'env:SUIYI_LOCAL_ANDROID_PASSWORD' --out $apk $source
    if ($LASTEXITCODE) { throw "apksigner failed ($LASTEXITCODE)." }
    & $signer verify --verbose --print-certs $apk
    if ($LASTEXITCODE) { throw "APK signature verification failed ($LASTEXITCODE)." }
    $hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$apk.sha256" -Encoding ascii -Value "$hash  $fileName"
    Write-Output (Get-Item -LiteralPath $apk).FullName
} finally {
    Remove-Item Env:SUIYI_LOCAL_ANDROID_PASSWORD -ErrorAction SilentlyContinue
}
