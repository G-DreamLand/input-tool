#requires -Version 5.1
<#
.SYNOPSIS
    发布 Input Tool：单文件自包含（首选项）+ 绿色目录包。

.DESCRIPTION
    对应《软件开发设计文档》§13.2 / §13.3。

    **为什么需要它**：build.ps1 产出的 dist\build-<NNN> 是「框架依赖的多文件发布」，
    其中的 InputTool.exe 只是 apphost，必须与同目录的 InputTool.dll 一起才能运行。
    所以"只把 exe 拷到别处"会直接报：

        The application to execute does not exist: '...\InputTool.dll'

    而且即便整个目录拷走，目标机器也必须预装 .NET 8 桌面运行时（否则同样打不开）。
    单文件自包含把运行时一起打进去，产出**一个 exe**，拷到哪都能双击运行。

.PARAMETER FrameworkDependent
    只出绿色目录版（框架依赖，体积小，但目标机需装 .NET 8 桌面运行时）。

.PARAMETER SingleFileOnly
    只出单文件自包含版（默认两种都出）。

.PARAMETER SkipTests
    跳过发布前的测试门禁。默认先跑 build/test.ps1，不通过就绝不发布。

.EXAMPLE
    pwsh -File build/publish.ps1
    pwsh -File build/publish.ps1 -SkipTests
    pwsh -File build/publish.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [switch]$FrameworkDependent,

    [switch]$SingleFileOnly,

    [switch]$SkipTests,

    # 单文件体积门禁（MB）。§13.4 发布前检查单：单文件 ≤ 100 MB
    [int]$MaxSingleFileMb = 100
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$dotnet = Get-DotNetPath
$root = Get-RepoRoot
$appProject = Join-Path $root 'src\InputTool.App\InputTool.App.csproj'
$coreDll = Join-Path $root "src\InputTool.Core\bin\$Configuration\net8.0-windows10.0.17763.0\InputTool.Core.dll"
$artifactsRoot = Join-Path $root "build\artifacts\$Runtime"

$wantSingle = -not $FrameworkDependent
$wantPortable = -not $SingleFileOnly

Write-Host "dotnet    : $dotnet" -ForegroundColor DarkGray
Write-Host "配置      : $Configuration / $Runtime" -ForegroundColor DarkGray

Stop-LeftoverTestHosts
$env:MSBUILDDISABLENODEREUSE = '1'

# ── 0) 测试门禁（§13.2 第 1 步）───────────────────────────────────────
if (-not $SkipTests) {
    Write-Host '发布前测试门禁：运行 build/test.ps1 …' -ForegroundColor DarkGray
    & (Join-Path $PSScriptRoot 'test.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "测试未通过（退出码 $LASTEXITCODE），已中止发布。"
    }
}
else {
    Write-Host '已按 -SkipTests 跳过测试门禁。' -ForegroundColor Yellow
}

# ── 发布（带文件占用重试；本机 360 实时扫描 DLL 是常态）─────────────
function Invoke-Publish {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string[]]$Arguments
    )

    $maxAttempts = 5
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        Write-Host "  $Title（第 $attempt 次尝试）…" -ForegroundColor DarkGray

        $output = & $dotnet publish @Arguments 2>&1
        $exitCode = $LASTEXITCODE
        if ($exitCode -eq 0) { return }

        $output | Write-Host
        if ($attempt -lt $maxAttempts -and (Test-FileLockError -Output $output)) {
            Write-Host '    检测到文件占用（杀软扫描 DLL），稍后重试…' -ForegroundColor Yellow
            Start-Sleep -Seconds 6
            continue
        }

        throw "$Title 失败，退出码 $exitCode"
    }
}

# Core 先单独构建：避免 WPF 内层 _wpftmp 工程与主工程图争用 Core.dll（见 build.ps1 的说明）
Write-Host '  构建 InputTool.Core' -ForegroundColor DarkGray
& $dotnet build (Join-Path $root 'src\InputTool.Core\InputTool.Core.csproj') `
    -c $Configuration --nologo -m:1 -nodeReuse:false -v quiet
if ($LASTEXITCODE -ne 0) { throw "InputTool.Core 构建失败，退出码 $LASTEXITCODE" }
Start-Sleep -Seconds 3

# ── 1) 单文件自包含（§13.3 首选项）────────────────────────────────────
$singleDir = Join-Path $artifactsRoot 'single'
if ($wantSingle) {
    if (Test-Path -LiteralPath $singleDir) { Remove-Item $singleDir -Recurse -Force }

    Write-Host '发布单文件自包含…' -ForegroundColor Cyan
    Invoke-Publish -Title '单文件自包含发布' -Arguments @(
        $appProject, '-c', $Configuration, '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:PublishReadyToRun=true',
        '-p:DebugType=none',
        '-p:BuildProjectReferences=false',
        '-o', $singleDir,
        '--nologo', '-m:1', '-nodeReuse:false', '-v', 'quiet'
    )
}

# ── 2) 绿色目录版（框架依赖，§13.3）───────────────────────────────────
$portableDir = Join-Path $artifactsRoot 'portable'
if ($wantPortable) {
    if (Test-Path -LiteralPath $portableDir) { Remove-Item $portableDir -Recurse -Force }

    Write-Host '发布绿色目录版（框架依赖）…' -ForegroundColor Cyan
    Invoke-Publish -Title '绿色目录版发布' -Arguments @(
        $appProject, '-c', $Configuration, '-r', $Runtime,
        '--self-contained', 'false',
        '-p:PublishSingleFile=false',
        '-p:BuildProjectReferences=false',
        '-o', $portableDir,
        '--nologo', '-m:1', '-nodeReuse:false', '-v', 'quiet'
    )

    # 绿色包该带上的说明与默认配置样例
    foreach ($doc in @('README.md', '使用说明.md', 'CHANGELOG.md')) {
        $src = Join-Path $root $doc
        if (Test-Path -LiteralPath $src) { Copy-Item $src $portableDir -Force }
    }
}

# ── 3) 单文件重命名 + 体积门禁（§13.4）───────────────────────────────
$singleExe = $null
if ($wantSingle) {
    $rawExe = Join-Path $singleDir 'InputTool.exe'
    if (-not (Test-Path -LiteralPath $rawExe)) { throw "单文件发布没有产出 InputTool.exe：$rawExe" }

    $version = (Get-Item -LiteralPath $rawExe).VersionInfo.FileVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = '1.0.0' }
    $version = ($version -split '\.')[0..2] -join '.'

    $singleExe = Join-Path $singleDir "InputTool-$version-$Runtime-single.exe"
    Move-Item -LiteralPath $rawExe -Destination $singleExe -Force

    # "单文件"就该只有一个文件：Core 是单独构建的，它的 pdb 会被发布流程捎带进来，
    # 留着会让交付目录看起来像需要多文件、也白占体积
    Get-ChildItem -LiteralPath $singleDir -File |
        Where-Object { $_.FullName -ne $singleExe } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $sizeMb = [Math]::Round((Get-Item -LiteralPath $singleExe).Length / 1MB, 1)
    Write-Host "  单文件：$([System.IO.Path]::GetFileName($singleExe))（$sizeMb MB）" -ForegroundColor DarkGray

    if ($sizeMb -gt $MaxSingleFileMb) {
        throw "单文件体积 $sizeMb MB 超过上限 $MaxSingleFileMb MB（§13.4 发布前检查单）。"
    }
}

# ── 4) 校验和（§13.3 SHA256SUMS.txt）─────────────────────────────────
$sumFile = Join-Path $artifactsRoot 'SHA256SUMS.txt'
$lines = @()
if ($wantSingle -and $singleExe) {
    $h = (Get-FileHash -LiteralPath $singleExe -Algorithm SHA256).Hash
    $lines += "$h  $([System.IO.Path]::GetFileName($singleExe))"
}
if ($wantPortable) {
    $zip = Join-Path $artifactsRoot "InputTool-$Runtime-portable.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath $zip -Force
    $h = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    $lines += "$h  $([System.IO.Path]::GetFileName($zip))"
    Write-Host "  绿色包：$([System.IO.Path]::GetFileName($zip))（$([Math]::Round((Get-Item $zip).Length/1MB,1)) MB）" -ForegroundColor DarkGray
}
Set-Content -LiteralPath $sumFile -Value ($lines -join "`r`n") -Encoding ASCII

Write-Host ''
Write-Host '发布完成。' -ForegroundColor Green
if ($wantSingle) { Write-Host "  单文件自包含（拷到哪都能用）：$singleExe" -ForegroundColor Green }
if ($wantPortable) { Write-Host "  绿色目录版（需 .NET 8 桌面运行时）：$portableDir" -ForegroundColor Green }
Write-Host "  校验和：$sumFile" -ForegroundColor DarkGray