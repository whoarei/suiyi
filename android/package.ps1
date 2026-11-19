# Builds and signs the sideload APK using the Windows user's Suiyi signing key.
[CmdletBinding()]
param([switch]$InitializeLocalKey)

$ErrorActionPreference = 'Stop'
if (-not $env:JAVA_HOME) { throw 'Set JAVA_HOME to a JDK 17 or 21 installation.' }
if (-not $env:ANDROID_HOME -and -not $env:ANDROID_SDK_ROOT) {
    throw 'Set ANDROID_HOME to the Android SDK directory.'
}
$keytool = Join-Path $env:JAVA_HOME 'bin/keytool.exe'
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

    $securePassword = ConvertTo-SecureString (Get-Content -LiteralPath $secret -Raw).Trim()
    $credential = [pscredential]::new('suiyi-android', $securePassword)
    $env:SUIYI_LOCAL_ANDROID_PASSWORD = $credential.GetNetworkCredential().Password
    & (Join-Path $PSScriptRoot 'sign-apk.ps1') -Keystore $keystore -StorePasswordEnv SUIYI_LOCAL_ANDROID_PASSWORD
} finally {
    Remove-Item Env:SUIYI_LOCAL_ANDROID_PASSWORD -ErrorAction SilentlyContinue
}
