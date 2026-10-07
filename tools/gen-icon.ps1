<#
.SYNOPSIS
    生成 Input Tool 的应用图标（多尺寸 ICO）。

.DESCRIPTION
    图标不在二进制仓库里手工维护，而是由本脚本可重复生成：
    蓝色圆角底 + 白色键盘图形，输出 16/24/32/48/64/128/256 七个尺寸。
    ICO 容器内各尺寸以 PNG 压缩存放（Vista+ 支持）。

.EXAMPLE
    powershell -File tools/gen-icon.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 注意：Windows PowerShell 5.1 在 param 默认值中取不到 $PSScriptRoot，因此在脚本体内解析路径。
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = Split-Path -Parent $scriptDir
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'src\InputTool.App\Assets\InputTool.ico'
}

$accent = [System.Drawing.Color]::FromArgb(255, 47, 111, 235)
$accentDeep = [System.Drawing.Color]::FromArgb(255, 31, 81, 179)
$white = [System.Drawing.Color]::White

function New-RoundedPath {
    param([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    # 底：圆角方块（对角渐变）
    $bg = New-RoundedPath -X 0 -Y 0 -W $Size -H $Size -R ($Size * 0.22)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.Point(0, 0)),
        (New-Object System.Drawing.Point($Size, $Size)),
        $accent, $accentDeep)
    $g.FillPath($brush, $bg)

    # 键盘机身
    $padX = $Size * 0.16
    $bodyW = $Size - ($padX * 2)
    $bodyH = $bodyW * 0.68
    $bodyY = ($Size - $bodyH) / 2
    $bodyR = [Math]::Max(1.0, $Size * 0.06)
    $body = New-RoundedPath -X $padX -Y $bodyY -W $bodyW -H $bodyH -R $bodyR
    $whiteBrush = New-Object System.Drawing.SolidBrush($white)
    $g.FillPath($whiteBrush, $body)

    # 键位：两行小方块
    $keyBrush = New-Object System.Drawing.SolidBrush($accent)
    $rows = 2
    $cols = 3
    $gapX = $bodyW * 0.14
    $gapY = $bodyH * 0.18
    $keyW = ($bodyW - ($gapX * ($cols + 1))) / $cols
    $keyH = ($bodyH - ($gapY * ($rows + 1))) / $rows
    for ($r = 0; $r -lt $rows; $r++) {
        for ($c = 0; $c -lt $cols; $c++) {
            $kx = $padX + $gapX + ($c * ($keyW + $gapX))
            $ky = $bodyY + $gapY + ($r * ($keyH + $gapY))
            $kr = [Math]::Max(0.5, $keyH * 0.25)
            $key = New-RoundedPath -X $kx -Y $ky -W $keyW -H $keyH -R $kr
            $g.FillPath($keyBrush, $key)
            $key.Dispose()
        }
    }

    $keyBrush.Dispose()
    $whiteBrush.Dispose()
    $body.Dispose()
    $brush.Dispose()
    $bg.Dispose()
    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = @()
foreach ($size in $sizes) {
    $bmp = New-IconBitmap -Size $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , @{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
    $bmp.Dispose()
}

# ── 组装 ICO 容器 ────────────────────────────────────────────────
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
}

$fs = [System.IO.File]::Create($OutputPath)
$bw = New-Object System.IO.BinaryWriter($fs)

$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type = icon
$bw.Write([uint16]$pngs.Count)    # image count

$headerSize = 6 + (16 * $pngs.Count)
$offset = $headerSize
foreach ($png in $pngs) {
    $dim = if ($png.Size -ge 256) { 0 } else { $png.Size }
    $bw.Write([byte]$dim)          # width（256 记 0）
    $bw.Write([byte]$dim)          # height
    $bw.Write([byte]0)             # palette
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # planes
    $bw.Write([uint16]32)          # bit count
    $bw.Write([uint32]$png.Bytes.Length)
    $bw.Write([uint32]$offset)
    $offset += $png.Bytes.Length
}
foreach ($png in $pngs) {
    $bw.Write($png.Bytes)
}

$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Host ("已生成 {0}（尺寸：{1}，共 {2} 字节）" -f $OutputPath, ($sizes -join '/'), (Get-Item -LiteralPath $OutputPath).Length) -ForegroundColor Green
