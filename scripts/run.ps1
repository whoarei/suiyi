param([switch]$Show, [switch]$Diagnostics, [switch]$Settings, [switch]$CheckUpdates)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $projectRoot 'artifacts\Tauri\TranslationFloat.exe'
if (-not (Test-Path -LiteralPath $appPath)) {
    $appPath = Join-Path $projectRoot 'src-tauri\target\release\TranslationFloat.exe'
}
if (-not (Test-Path -LiteralPath $appPath)) { throw '请先运行 scripts\build.ps1。' }
$appArguments = @()
if ($Show) { $appArguments += '--show' }
if ($Diagnostics) { $appArguments += '--diagnostics' }
if ($Settings) { $appArguments += '--settings' }
if ($CheckUpdates) { $appArguments += '--check-updates' }
$startOptions = @{ FilePath = $appPath; WorkingDirectory = $projectRoot; WindowStyle = 'Hidden'; PassThru = $true }
if ($appArguments.Count -gt 0) { $startOptions.ArgumentList = $appArguments }
$started = Start-Process @startOptions
Write-Output "已启动。请在目标输入框按 Ctrl+Alt+T。进程：$($started.Id)"
