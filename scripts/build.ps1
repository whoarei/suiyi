param([string]$Version, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not (Test-Path -LiteralPath 'node_modules/@tauri-apps/cli')) {
        npm ci --ignore-scripts
        if ($LASTEXITCODE -ne 0) { throw 'Tauri CLI 安装失败。' }
    }
    npm run check
    if ($LASTEXITCODE -ne 0) { throw 'Tauri 检查失败。' }
    $arguments = @('build','--no-bundle')
    $env:SUIYI_BUILD_VERSION = if ($Version) { $Version } else { (Get-Content src-tauri/tauri.conf.json -Raw | ConvertFrom-Json).version }
    if ($Version) {
        $configPath = Join-Path $projectRoot ('artifacts\tauri-version-' + [Guid]::NewGuid().ToString('N') + '.json')
        New-Item -ItemType Directory -Path (Split-Path -Parent $configPath) -Force | Out-Null
        [IO.File]::WriteAllText($configPath, (@{version=$Version} | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
        $arguments += @('--config', $configPath)
    }
    & node_modules/.bin/tauri.cmd @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Tauri 构建失败。' }
    $outputPath = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'artifacts\Tauri' }
    if ((Test-Path -LiteralPath $outputPath) -and @(Get-ChildItem -LiteralPath $outputPath -Recurse -Force | Where-Object { $_.Name -notin @('TranslationFloat.exe','使用说明.txt') }).Count -ne 0) {
        throw '输出目录含其他文件。请使用新的空目录，避免将旧 WPF 运行时混入 Tauri 发布包。'
    }
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
    Copy-Item -LiteralPath 'src-tauri/target/release/TranslationFloat.exe' -Destination $outputPath
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\quickstart.txt') -Destination (Join-Path $outputPath '使用说明.txt') -Force
    Write-Output "程序：$outputPath\TranslationFloat.exe"
}
finally { Pop-Location }
