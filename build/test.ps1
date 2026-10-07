#requires -Version 5.1
<#
.SYNOPSIS
    运行单元测试。

.DESCRIPTION
    为什么用 `dotnet run` 而不是 `dotnet test`：
      · 测试栈是 xUnit v3 + Microsoft.Testing.Platform，测试工程本身就是可执行程序；
      · `dotnet test` 会让测试进程通过**命名管道**回连 MSBuild，
        而本会话环境禁用了命名管道，会以 "NamedPipeClientStream.TryConnect" 失败告终；
      · `dotnet run` 直接在该进程内运行测试，链路更短且不受该限制影响。

.EXAMPLE
    pwsh -File build/test.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # 直接透传给测试宿主的参数，例如 --filter / --list-tests
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$TestArguments
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$dotnet = Get-DotNetPath
$root = Get-RepoRoot
$solution = Join-Path $root 'src\InputTool.sln'

Stop-LeftoverTestHosts

# 复用 build.ps1 的构建流程，而不是自己 dotnet build <sln>：
# 后者会触发 WPF 内层工程与其它工程争用 Core.dll（MC1000 / CS0009 / CS2012），
# 甚至把程序集写坏，导致测试宿主以 0xE0434352 崩溃。
Write-Host '构建测试工程…' -ForegroundColor DarkGray
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration -NoRestore -SkipPublish

$failed = 0
foreach ($project in @('InputTool.Tests', 'InputTool.IntegrationTests')) {
    $projectPath = Join-Path $root "src\$project"
    Write-Host ""
    Write-Host "=== $project ===" -ForegroundColor Cyan

    # 直接运行测试程序集而不是 dotnet run：
    # 1) 省掉 apphost 的复制步骤；2) 避免测试宿主未退出时锁住 bin 下的 exe 导致后续构建失败。
    $testAssembly = Get-ChildItem (Join-Path $projectPath "bin\$Configuration") -Recurse -Filter "$project.dll" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $testAssembly) {
        throw "找不到测试程序集：$projectPath\bin\$Configuration"
    }

    & $dotnet $testAssembly.FullName @TestArguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$project 测试失败，退出码 $LASTEXITCODE" -ForegroundColor Red
        $failed++
    }
}

Stop-LeftoverTestHosts

if ($failed -gt 0) {
    throw "$failed 个测试工程失败。"
}

Write-Host ""
Write-Host '全部测试通过。' -ForegroundColor Green
