param([Parameter(Mandatory)][string]$AppDirectory, [string]$PreviousAppDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = & (Join-Path $PSScriptRoot 'ensure-inno.ps1')
$dotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { $dotnet = (Get-Command dotnet -ErrorAction Stop).Source }
$identity = 'Suiyi.Installer.Smoke.' + [Guid]::NewGuid().ToString('N')
$root = Join-Path $projectRoot ('artifacts\' + $identity)
$installed = Join-Path $root 'installed'
$mutex = 'Local\' + $identity
$exitEvent = $mutex + '.Exit'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $identity + '_is1'
$webviewKey = 'Software\' + $identity + '\WebView'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$fixture = $null

function Invoke-Setup([string]$Executable, [string[]]$Options) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Options -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(45000)) { $process.Kill(); throw '安装器测试超时。' }
    if ($process.ExitCode -ne 0) { throw "安装器测试失败：退出代码 $($process.ExitCode)" }
}
function Build-Setup([string]$Source, [string]$Version) {
    $fileVersion = ($Version -split '-')[0] + '.0'
    $buildOptions = @('/Qp','/DSmokeTest=1')
    if ($Version -ne '0.2.0') { $buildOptions += @('/DTauriBuild=1', "/DWebViewProbeKey=$webviewKey") }
    & $compiler @buildOptions "/DAppIdentity=$identity" "/DMutexName=$mutex" "/DExitEventName=$exitEvent" "/DInstallDirectory=$installed" "/DAppVersion=$Version" "/DAppFileVersion=$fileVersion" "/DAppSource=$Source" "/DSetupOutput=$root" (Join-Path $projectRoot 'installer\Suiyi.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '隔离测试安装包构建失败。' }
    return Join-Path $root "suiyi-$Version-windows-x64-setup.exe"
}
function Start-Fixture([string]$ReadyName) {
    $ready = Join-Path $root $ReadyName
    $fixtureDll = Join-Path $root 'fixture\TranslationFloat.InstallerFixture.dll'
    $lockPath = Join-Path $installed 'TranslationFloat.exe'
    $arguments = @(('"' + $fixtureDll + '"'), $mutex, $exitEvent, ('"' + $lockPath + '"'), ('"' + $ready + '"'))
    $process = Start-Process -FilePath $dotnet -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $ready)) {
        if ($process.HasExited -or $watch.Elapsed.TotalSeconds -gt 10) { throw '安装器运行中夹具启动失败。' }
        Start-Sleep -Milliseconds 100
    }
    return $process
}
try {
    & $dotnet publish (Join-Path $projectRoot 'tools\TranslationFloat.InstallerFixture\TranslationFloat.InstallerFixture.csproj') -c Release -o (Join-Path $root 'fixture') --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '测试夹具构建失败。' }
    $oldSource = if ($PreviousAppDirectory) { $PreviousAppDirectory } else { $AppDirectory }
    $oldSetup = Build-Setup (Resolve-Path -LiteralPath $oldSource).Path '0.2.0'
    $newSetup = Build-Setup (Resolve-Path -LiteralPath $AppDirectory).Path '0.3.0-dev'
    $options = @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART')
    Invoke-Setup $oldSetup ($options + ('/LOG="' + (Join-Path $root 'install.log') + '"'))
    if (-not (Test-Path -LiteralPath (Join-Path $installed 'TranslationFloat.exe'))) { throw '首次安装缺少程序。' }
    $config = Join-Path $root 'fake-settings.json'
    [System.IO.File]::WriteAllText($config, '{"fake":"preserve-me"}')
    $keepFile = Join-Path $installed 'keep.txt'
    [System.IO.File]::WriteAllText($keepFile, 'unmanaged test file')
    Write-Output 'PASS 安装到隔离目录并创建独立卸载项'
    [System.IO.File]::WriteAllText((Join-Path $installed 'TranslationFloat.exe'), 'old fixture payload to replace')
    # CI may use the current Rust payload as the old source; seed managed WPF
    # files so the real installer must still exercise legacy-runtime cleanup.
    [System.IO.File]::WriteAllText((Join-Path $installed 'coreclr.dll'), 'legacy runtime fixture')
    [System.IO.File]::WriteAllText((Join-Path $installed 'TranslationFloat.dll'), 'legacy assembly fixture')
    $fixture = Start-Fixture 'upgrade.ready'
    Invoke-Setup $newSetup ($options + '/SUIYIUPDATE=1' + ('/LOG="' + (Join-Path $root 'upgrade.log') + '"'))
    if (-not $fixture.WaitForExit(5000) -or $fixture.ExitCode -ne 0) { throw '升级未正常关闭夹具。' }
    $fixture = $null
    $actual = Get-FileHash -LiteralPath (Join-Path $installed 'TranslationFloat.exe') -Algorithm SHA256
    $expected = Get-FileHash -LiteralPath (Join-Path (Resolve-Path -LiteralPath $AppDirectory).Path 'TranslationFloat.exe') -Algorithm SHA256
    if ($actual.Hash -ne $expected.Hash -or -not (Test-Path -LiteralPath $keepFile)) { throw '覆盖升级内容不符。' }
    Write-Output 'PASS 安装器通过退出事件关闭持锁夹具并完成覆盖升级'
    if (Test-Path -LiteralPath (Join-Path $installed 'coreclr.dll')) { throw '升级未清理旧 WPF 运行时。' }
    Write-Output 'PASS 覆盖升级清理旧 WPF 托管文件并保留非托管文件'
    if ((Get-ItemPropertyValue -LiteralPath ('HKCU:\' + $webviewKey) -Name InstallCount) -ne 1) { throw '缺少 WebView2 时未调用安装替身。' }
    Invoke-Setup $newSetup ($options + ('/LOG="' + (Join-Path $root 'webview-present.log') + '"'))
    if ((Get-ItemPropertyValue -LiteralPath ('HKCU:\' + $webviewKey) -Name InstallCount) -ne 1) { throw '已有 WebView2 时重复安装。' }
    Write-Output 'PASS WebView2 缺失时调用隔离安装替身，已有时跳过安装'
    $fixture = Start-Fixture 'uninstall.ready'
    Invoke-Setup (Join-Path $installed 'unins000.exe') ($options + ('/LOG="' + (Join-Path $root 'uninstall.log') + '"'))
    if (-not $fixture.WaitForExit(5000) -or $fixture.ExitCode -ne 0) { throw '卸载未正常关闭夹具。' }
    $fixture = $null
    if (Test-Path -LiteralPath (Join-Path $installed 'TranslationFloat.exe')) { throw '卸载后程序仍存在。' }
    if ((Get-Content -LiteralPath $config -Raw) -ne '{"fake":"preserve-me"}' -or -not (Test-Path -LiteralPath $keepFile)) { throw '卸载删除了配置或非托管文件。' }
    if (Test-Path -LiteralPath ('HKCU:\' + $uninstallKey)) { throw '卸载项未清理。' }
    Write-Output 'PASS 卸载关闭持锁夹具，清理程序并保留非托管文件和外部虚构配置'
    Write-Output "安装器隔离检查通过，日志：$root"
}
finally {
    if ($fixture -and -not $fixture.HasExited) { $fixture.Kill(); $fixture.WaitForExit() }
    if (Test-Path -LiteralPath ('HKCU:\' + $webviewKey)) { Remove-Item -LiteralPath ('HKCU:\' + $webviewKey) }
}
