#requires -Version 5.1
<#
.SYNOPSIS
    定位本机可用的 .NET SDK（dotnet 可执行文件）。

.DESCRIPTION
    本项目开发环境中 .NET 8 SDK 以免管理员方式安装在用户目录：
        %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe
    若该路径不存在，则回退到 PATH 上的 dotnet。
    被 build.ps1 / test.ps1 点源（dot-source）复用。
#>

function Get-DotNetPath {
    [CmdletBinding()]
    param()

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'),
        (Join-Path ${env:ProgramFiles} 'dotnet\dotnet.exe')
    )

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    throw '未找到 .NET SDK。请安装 .NET 8 SDK，或确认 %LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe 存在。'
}

function Get-RepoRoot {
    [CmdletBinding()]
    param()
    # 本文件位于 <repo>/build/ 下
    return (Split-Path -Parent $PSScriptRoot)
}

function Stop-LeftoverTestHosts {
    [CmdletBinding()]
    param()

    # 1) 测试宿主与主程序进程若未退出，会锁住 bin 下的 exe，导致下一次生成报 MSB3021/MSB3027。
    $names = @('InputTool', 'InputTool.Tests', 'InputTool.IntegrationTests')
    foreach ($name in $names) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }

    # 2) 残留的构建进程（MSBuild 节点 / Roslyn 编译器服务）会持有 Core.dll，
    #    导致下游项目报 MSB3248 / CS0009「文件正被另一进程使用」。
    #    只结束本机 SDK 目录下（或路径不可读）的那些，避免误伤其它程序。
    $sdkRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    foreach ($name in @('dotnet', 'MSBuild', 'VBCSCompiler')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object {
            -not $_.Path -or $_.Path.StartsWith($sdkRoot, [StringComparison]::OrdinalIgnoreCase)
        } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Wait-FileUnlocked {
    <#
    .SYNOPSIS
        等到指定文件可以被独占打开（即没有别的进程占着它）。

    .DESCRIPTION
        WPF 的标记编译会派生内层工程去加载被引用程序集，构建刚结束时
        InputTool.Core.dll 可能还被短暂占用。此时开始构建下游工程会直接失败：

            MC1000 / CS0009：The process cannot access the file '...Core.dll'
                              because it is being used by another process.

        与其失败后让用户重试，不如在构建每个工程之前先等它解锁。
        文件不存在视为"已解锁"（首次构建的正常情况）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [int]$TimeoutSeconds = 20
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return $true
        }

        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Close()
            $stream.Dispose()
            return $true
        }
        catch [System.IO.IOException] {
            Start-Sleep -Milliseconds 300
        }
        catch [System.UnauthorizedAccessException] {
            # 权限问题不是"被占用"，不必等待
            return $true
        }
    }

    Write-Warning "等待文件解锁超时：$Path"
    return $false
}

function Test-FileLockError {
    <#
    .SYNOPSIS
        判断构建输出里是否包含"文件正被另一进程使用"这类错误。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]]$Output
    )

    $text = $Output -join "`n"
    return ($text -match 'being used by another process') -or
           ($text -match '正被另一进程使用') -or
           ($text -match 'MSB3021') -or
           ($text -match 'MSB3027') -or
           ($text -match 'MC1000')
}
