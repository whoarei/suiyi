param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetPath = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $projectRoot
try {
    & $dotnetPath run --project 'tests\TranslationFloat.Core.Tests\TranslationFloat.Core.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw '回填逻辑检查失败。' }
    & $dotnetPath run --project 'tests\TranslationFloat.Translation.Tests\TranslationFloat.Translation.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw '翻译协议或调度检查失败。' }
    & $dotnetPath run --project 'tests\TranslationFloat.Native.Tests\TranslationFloat.Native.Tests.csproj' -c Release
    if ($LASTEXITCODE -ne 0) { throw '原生输入结构检查失败。' }
    $outputName = if ($FrameworkDependent) { 'TranslationFloat-framework-dependent' } else { 'TranslationFloat' }
    $outputPath = Join-Path $projectRoot ('artifacts\' + $outputName)
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    & $dotnetPath publish 'src\TranslationFloat.App\TranslationFloat.App.csproj' -c Release -r win-x64 --self-contained $selfContained -o $outputPath --nologo
    if ($LASTEXITCODE -ne 0) { throw '发布文件生成失败。' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\quickstart.txt') -Destination (Join-Path $outputPath '使用说明.txt') -Force
    Write-Output "程序：$outputPath\TranslationFloat.exe"
}
finally { Pop-Location }
