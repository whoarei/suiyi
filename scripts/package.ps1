param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')]
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
# Each build starts with an empty directory; old published files cannot enter a new package.
$stagingPath = Join-Path $projectRoot ('artifacts\package-' + [Guid]::NewGuid().ToString('N'))
$appPath = Join-Path $stagingPath 'Suiyi'
$releasePath = Join-Path $projectRoot 'artifacts\release'
New-Item -ItemType Directory -Path $appPath, $releasePath -Force | Out-Null

& (Join-Path $PSScriptRoot 'build.ps1') -Version $Version -OutputDirectory $appPath
foreach ($requiredFile in @('TranslationFloat.exe', 'TranslationFloat.dll', 'TranslationFloat.runtimeconfig.json', 'coreclr.dll', 'PresentationFramework.dll', '使用说明.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appPath $requiredFile) -PathType Leaf)) {
        throw "便携包缺少必需文件：$requiredFile"
    }
}
$runtime = Get-Content -LiteralPath (Join-Path $appPath 'TranslationFloat.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or $runtime.runtimeOptions.frameworks) {
    throw '发布目录不是自包含版本。'
}
$unexpected = Get-ChildItem -LiteralPath $appPath -File -Recurse | Where-Object {
    $_.Name -in @('deepseek.key', 'settings.json', 'm0.log') -or $_.Name -like 'api-*.key' -or $_.Name -like '.env*'
}
if ($unexpected) { throw '发布目录包含本机配置或凭据文件。' }

$archiveName = "suiyi-$Version-windows-x64-portable.zip"
$archivePath = Join-Path $releasePath $archiveName
Compress-Archive -LiteralPath $appPath -DestinationPath $archivePath -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$archivePath.sha256", "$hash  $archiveName`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "便携包：$archivePath"
Write-Output "SHA-256：$hash"
