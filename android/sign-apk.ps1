[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Keystore,
    [string]$KeyAlias = 'suiyi-android',
    [string]$StorePasswordEnv = 'ANDROID_KEYSTORE_PASSWORD',
    [string]$KeyPasswordEnv = $StorePasswordEnv,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/android')
)

$ErrorActionPreference = 'Stop'
foreach ($passwordEnv in @($StorePasswordEnv, $KeyPasswordEnv) | Select-Object -Unique) {
    if (-not [Environment]::GetEnvironmentVariable($passwordEnv)) {
        throw "Missing signing password in environment variable $passwordEnv."
    }
}

$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { $env:ANDROID_SDK_ROOT }
if (-not $sdk) { throw 'Set ANDROID_HOME to the Android SDK directory.' }
$signerName = if ($env:OS -eq 'Windows_NT') { 'apksigner.bat' } else { 'apksigner' }
$signer = Join-Path $sdk "build-tools/36.0.0/$signerName"

$releaseDir = Join-Path $PSScriptRoot 'app/build/outputs/apk/release'
$metadata = Get-Content -LiteralPath (Join-Path $releaseDir 'output-metadata.json') -Raw |
    ConvertFrom-Json
$outputs = @($metadata.elements)
if ($outputs.Count -ne 1) { throw 'Expected one Android release APK.' }
$output = $outputs[0]
$source = Join-Path $releaseDir $output.outputFile

$artifactDir = New-Item -ItemType Directory -Force -Path $OutputDirectory
$fileName = "suiyi-$($output.versionName)-android.apk"
$apk = Join-Path $artifactDir.FullName $fileName
& $signer sign --ks $Keystore --ks-key-alias $KeyAlias `
    --ks-pass "env:$StorePasswordEnv" --key-pass "env:$KeyPasswordEnv" `
    --v4-signing-enabled false --out $apk $source
if ($LASTEXITCODE) { throw "apksigner failed ($LASTEXITCODE)." }

& $signer verify --verbose --print-certs $apk
if ($LASTEXITCODE) { throw "APK signature verification failed ($LASTEXITCODE)." }

$hash = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$apk.sha256", "$hash  $fileName`n")
Write-Output (Get-Item -LiteralPath $apk).FullName
