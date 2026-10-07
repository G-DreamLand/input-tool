#requires -Version 5.1
<#
.SYNOPSIS
    构建 Input Tool 解决方案。

.DESCRIPTION
    **逐个工程串行构建**，而不是直接 `dotnet build InputTool.sln`。
    原因（本机实测，见设计文档 §21.10）：

    WPF 的标记编译会为 InputTool.App 派生一个形如
    `InputTool.App_<hash>_wpftmp.csproj` 的临时工程，它会**独立于主工程图**去解析并加载
    InputTool.Core.dll。当同一个构建里还有别的工程同时在读同一个 Core.dll 时，
    就会报：

        MC1000 / CS0009：The process cannot access the file 'InputTool.Core.dll'
                          because it is being used by another process.

    实测：`-m:1`、`-nodeReuse:false`、`-p:UseSharedCompilation=false` 都**不足以**消除它；
    而"一个工程一个工程地构建（各自带上自己的依赖）"稳定通过。
    本解决方案只有 4 个工程，这点串行开销完全可以接受。

.EXAMPLE
    pwsh -File build/build.ps1
    pwsh -File build/build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$NoRestore,

    # 跳过"发布到带编译次数的目录"这一步（日常只编译不发布时用）
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$dotnet = Get-DotNetPath
$root = Get-RepoRoot
$solution = Join-Path $root 'src\InputTool.sln'

# 按依赖顺序：Core → App → Tests → IntegrationTests
$projects = @('InputTool.Core', 'InputTool.App', 'InputTool.Tests', 'InputTool.IntegrationTests')

Write-Host "dotnet  : $dotnet" -ForegroundColor DarkGray
Write-Host "解决方案: $solution" -ForegroundColor DarkGray
Write-Host "配置    : $Configuration" -ForegroundColor DarkGray

Stop-LeftoverTestHosts

# 关闭 MSBuild 节点复用：常驻节点会在构建结束后继续持有 Core.dll，
# 导致下一次构建报「文件正被另一进程使用」。
$env:MSBUILDDISABLENODEREUSE = '1'

if (-not $NoRestore) {
    Write-Host '还原依赖…' -ForegroundColor DarkGray
    & $dotnet restore $solution --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "依赖还原失败，退出码 $LASTEXITCODE"
    }
}

$targetFramework = 'net8.0-windows10.0.17763.0'

# 重试预算：本机装有 360 安全卫士（360rp.exe），它会实时扫描新生成的 DLL 并短暂持有句柄，
# 因此"文件正被另一进程使用"在本机是常态而非异常。多给几次机会、每次多等一会儿，
# 并逐次清理中间产物。**根本解法是把本目录加入 360 的信任/排除列表**（见 README）。
$maxAttempts = 10
$settleSeconds = 4

foreach ($project in $projects) {
    $projectPath = Join-Path $root "src\$project\$project.csproj"
    $outputDirectory = Join-Path $root "src\$project\bin\$Configuration\$targetFramework"

    Write-Host "  构建 $project" -ForegroundColor DarkGray

    # 依赖输出必须先解锁，否则本工程会读到一半被占用的 dll。
    # 360 的实时扫描可能持续数十秒，这里给足耐心。
    foreach ($dependency in @('InputTool.Core', $project)) {
        $dependencyDll = Join-Path $root "src\$dependency\bin\$Configuration\$targetFramework\$dependency.dll"
        Wait-FileUnlocked -Path $dependencyDll -TimeoutSeconds 60 | Out-Null
    }

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        # -p:BuildProjectReferences=false：**这是本机稳定构建的关键**。
        # 下游工程（App / Tests / IntegrationTests）不再重建 Core，只读取它已生成的产物。
        # 否则 WPF 为 App 派生的 _wpftmp 内层工程会与主工程图**各自去构建 Core**，
        # 两边同时读写同一个 InputTool.Core.dll，必然报
        # MC1000 / CS0009 / CS2012「文件正被另一进程使用」。
        # 我们已经在本循环开头单独构建过 Core，所以这里不再需要它重复构建。
        $extraArguments = @()
        if ($project -ne 'InputTool.Core') {
            $extraArguments += '-p:BuildProjectReferences=false'
        }

        $output = & $dotnet build $projectPath `
            -c $Configuration `
            --nologo `
            --no-restore `
            -m:1 `
            -nodeReuse:false `
            -v quiet @extraArguments 2>&1

        $exitCode = $LASTEXITCODE
        $output | Write-Host

        if ($exitCode -eq 0) {
            break
        }
        # 文件占用是常态：本机 360 安全卫士（360rp.exe）会实时扫描新生成的 DLL 并短暂持有句柄。
        # 先等一等重试；若仍失败，说明 WPF 临时工程的中间状态已被写坏（obj 里的残留会让
        # _wpftmp 工程**持续**冲突），此时必须清掉本工程的 obj/bin 再来——
        # 注意 obj 里有 project.assets.json，清完必须重新还原，否则下一次会报 NETSDK1004。
        if ($attempt -lt $maxAttempts -and (Test-FileLockError -Output $output)) {
            Write-Host "    检测到文件占用（360 实时防护正在扫描 DLL），清理中间产物后重试（第 $($attempt + 1) 次）…" -ForegroundColor Yellow

            Remove-Item (Join-Path $root "src\$project\obj") -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item (Join-Path $root "src\$project\bin") -Recurse -Force -ErrorAction SilentlyContinue

            & $dotnet restore $projectPath --nologo -v quiet | Out-Null

            Wait-FileUnlocked -Path (Join-Path $outputDirectory "$project.dll") | Out-Null
            Start-Sleep -Seconds $settleSeconds
            continue
        }

        throw "$project 构建失败，退出码 $exitCode"
    }

    # 给杀软留出扫描时间：下一个工程马上读这个刚生成的 dll 时，
    # 360 往往还拿着句柄不放。
    Start-Sleep -Seconds $settleSeconds
}

Write-Host '构建成功。' -ForegroundColor Green

# ── 发布到「带编译次数」的目录，并且保留最近 3 次结果 ──────────────
if (-not $SkipPublish) {
    # 编译次数持久化在 build/.build-count（本地文件，不进版本库）
    $countFile = Join-Path $PSScriptRoot '.build-count'
    $buildNumber = 0
    if (Test-Path -LiteralPath $countFile) {
        $previous = Get-Content -LiteralPath $countFile -Raw -ErrorAction SilentlyContinue
        if ($previous) { [void][int]::TryParse($previous.Trim(), [ref]$buildNumber) }
    }

    $buildNumber++
    Set-Content -LiteralPath $countFile -Value $buildNumber -Encoding ASCII

    $distRoot = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force -Path $distRoot | Out-Null

    # 保留最近 3 次编译结果：先删掉更早的（这里保留 2 个，加上本次发布正好 3 个）
    Get-ChildItem -LiteralPath $distRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -Skip 2 |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    $outputDir = Join-Path $distRoot ("build-{0:D3}" -f $buildNumber)
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    Write-Host "  发布第 $buildNumber 次编译 → dist\build-$('{0:D3}' -f $buildNumber)" -ForegroundColor DarkGray

    & $dotnet publish (Join-Path $root 'src\InputTool.App\InputTool.App.csproj') `
        -c $Configuration `
        -r win-x64 `
        --self-contained false `
        -o $outputDir `
        --nologo `
        -m:1 `
        -nodeReuse:false `
        -v quiet

    if ($LASTEXITCODE -ne 0) {
        throw "发布失败，退出码 $LASTEXITCODE"
    }

    # 把编译次数与关键路径一并写进产物目录，方便随时对照
    $buildInfo = @(
        "编译次数 : $buildNumber",
        "编译时间 : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
        "配置     : $Configuration",
        "运行时   : win-x64（框架依赖，需要已安装 .NET 8）",
        "启动     : 双击 InputTool.exe",
        "配置文件 : %APPDATA%\InputTool\config.json",
        "日志目录 : %APPDATA%\InputTool\logs（写不进去时自动降级到 %TEMP%\InputTool-logs）"
    ) -join "`r`n"

    Set-Content -LiteralPath (Join-Path $outputDir 'BUILD-INFO.txt') -Value $buildInfo -Encoding UTF8

    # 若超出保留次数的旧产物因为程序正在运行而删不掉，明确提醒
    $leftover = Get-ChildItem -LiteralPath $distRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        Select-Object -Skip 3

    if ($leftover) {
        Write-Host ("  提示：以下旧产物未能删除（多半是程序还在运行）：" + (($leftover | ForEach-Object { $_.Name }) -join '、')) -ForegroundColor Yellow
    }

    Write-Host "  第 $buildNumber 次编译结果：dist\$([System.IO.Path]::GetFileName($outputDir))" -ForegroundColor Green
}
