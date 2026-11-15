param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$')][string]$Version,
    [Parameter(Mandatory)][string]$AppDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = (Resolve-Path -LiteralPath $AppDirectory).Path
$outputPath = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\release' }
foreach ($name in @('TranslationFloat.exe','使用说明.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appPath $name) -PathType Leaf)) { throw "安装源缺少文件：$name" }
}
$compiler = & (Join-Path $PSScriptRoot 'ensure-inno.ps1')
if (Test-Path -LiteralPath (Join-Path $appPath 'coreclr.dll')) { throw 'Tauri 安装源不能包含旧 .NET 运行时。' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$fileVersion = ($Version -split '-')[0] + '.0'
& $compiler '/Qp' '/DTauriBuild=1' "/DAppVersion=$Version" "/DAppFileVersion=$fileVersion" "/DAppSource=$appPath" "/DSetupOutput=$outputPath" (Join-Path $projectRoot 'installer\Suiyi.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包构建失败。' }
$setupPath = Join-Path $outputPath "suiyi-$Version-windows-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) { throw '没有生成安装包。' }
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText("$setupPath.sha256", "$hash  $([System.IO.Path]::GetFileName($setupPath))`n", [System.Text.UTF8Encoding]::new($false))
Write-Output "安装包：$setupPath"
Write-Output "SHA-256：$hash"
