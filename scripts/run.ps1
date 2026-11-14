param([switch]$Show, [switch]$Diagnostics, [switch]$Settings)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot 'artifacts\TranslationFloat\TranslationFloat.exe'
if (-not (Test-Path -LiteralPath $appPath)) {
    $appPath = Join-Path $projectRoot 'src\TranslationFloat.App\bin\Release\net10.0-windows\win-x64\TranslationFloat.exe'
}
if (-not (Test-Path -LiteralPath $appPath)) { throw '请先运行 scripts\build.ps1。' }
$appArguments = @()
if ($Show) { $appArguments += '--show' }
if ($Diagnostics) { $appArguments += '--diagnostics' }
if ($Settings) { $appArguments += '--settings' }
$startOptions = @{ FilePath = $appPath; WorkingDirectory = $projectRoot; WindowStyle = 'Hidden'; PassThru = $true }
if ($appArguments.Count -gt 0) { $startOptions.ArgumentList = $appArguments }
$started = Start-Process @startOptions
Write-Output "已启动。请在目标输入框按 Ctrl+Alt+T。进程：$($started.Id)"
