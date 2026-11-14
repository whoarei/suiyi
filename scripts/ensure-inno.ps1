$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '6.7.3'
$toolDirectory = Join-Path $projectRoot ".tools\inno-$version"
$compiler = Join-Path $toolDirectory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $download = Join-Path $projectRoot ".tools\innosetup-$version.exe"
    New-Item -ItemType Directory -Path (Split-Path -Parent $download) -Force | Out-Null
    $expectedHash = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
    if (-not (Test-Path -LiteralPath $download) -or (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) {
        Invoke-WebRequest -Uri "https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-$version.exe" -OutFile $download
    }
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Inno Setup 下载校验失败。' }
    # Official portable mode: no compiler uninstall entry, shortcuts, or file associations.
    $process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/PORTABLE=1','/CURRENTUSER',('/DIR="' + $toolDirectory + '"')) -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Inno Setup 编译器准备失败。' }
}
$languageFile = Join-Path $toolDirectory 'Languages\ChineseSimplified.isl'
$languageHash = '7d544b9bb1d142cfa11f2e5d3cc8abe2e55f8e066c5124e3772675aa236e1278'
if (-not (Test-Path -LiteralPath $languageFile) -or (Get-FileHash -LiteralPath $languageFile -Algorithm SHA256).Hash -ne $languageHash) {
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_3/Files/Languages/Unofficial/ChineseSimplified.isl' -OutFile $languageFile
    if ((Get-FileHash -LiteralPath $languageFile -Algorithm SHA256).Hash -ne $languageHash) { throw '安装器中文语言文件校验失败。' }
}
$compiler
