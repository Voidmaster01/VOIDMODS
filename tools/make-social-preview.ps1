<#
  make-social-preview.ps1 - draws the GitHub "Social Preview" banner for VOIDMODS (1280 x 640, the size GitHub asks for).

  HOW TO RUN (PowerShell 7, from the repo folder):   pwsh -NoProfile -File tools\make-social-preview.ps1
  WHAT IT NEEDS: PowerShell 7 on Windows (System.Drawing, Windows' drawing library) and branding\voidmod.png.
  WHAT IT MAKES: branding\social-preview.png. Upload it on github.com: repo Settings -> General -> Social preview.
  To list new mods on the banner, add them to $mods below and run it again.

  HOW IT DRAWS (like painting layers from back to front):
    1. background: near-black purple, with a soft purple glow behind the logo (a "path gradient": a color that
       fades out from a center point), and a frame like the game's menu box
    2. the V logo on the left, scaled up with high-quality smoothing
    3. "VOIDMODS", the subtitle, and one menu-style row per mod (dark box, border, purple ">")
    GitHub crops nothing, but shows the banner small in link previews, so everything important stays big and
    at least 40 pixels away from the edges.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
$W = 1280; $H = 640

# The mods shown as rows: name and a few words about it.
$mods = @(
    @{ Name = 'MOD CONFIGS'; About = 'in-game settings hub' },
    @{ Name = 'VOIDCONS';    About = 'your own HD player icon' }
)

# Colors (the same purples as UiKit.cs in ModConfigs). FromArgb(alpha, red, green, blue), each 0-255.
$bgColor     = [System.Drawing.Color]::FromArgb(255, 9, 5, 15)
$rowColor    = [System.Drawing.Color]::FromArgb(255, 15, 9, 26)
$borderColor = [System.Drawing.Color]::FromArgb(255, 43, 28, 69)
$purple      = [System.Drawing.Color]::FromArgb(255, 156, 92, 230)
$white       = [System.Drawing.Color]::FromArgb(255, 242, 242, 247)
$dim         = [System.Drawing.Color]::FromArgb(255, 140, 133, 158)

# A font that exists on every Windows PC; the first one found in this list is used.
function Get-Font([string[]]$names, [float]$size, [System.Drawing.FontStyle]$style) {
    $installed = [System.Drawing.Text.InstalledFontCollection]::new().Families.Name
    foreach ($n in $names) { if ($installed -contains $n) { return [System.Drawing.Font]::new($n, $size, $style, [System.Drawing.GraphicsUnit]::Pixel) } }
    return [System.Drawing.Font]::new('Arial', $size, $style, [System.Drawing.GraphicsUnit]::Pixel)
}

$bmp = [System.Drawing.Bitmap]::new($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
$g.TextRenderingHint = 'AntiAliasGridFit'

# 1. Background, glow and frame.
$g.Clear($bgColor)
$glowArea = [System.Drawing.Drawing2D.GraphicsPath]::new()
$glowArea.AddEllipse(-60, -40, 640, 720)                      # an oval around the logo's spot
$glow = [System.Drawing.Drawing2D.PathGradientBrush]::new($glowArea)
$glow.CenterColor = [System.Drawing.Color]::FromArgb(110, 120, 60, 200)   # see-through purple in the middle...
$glow.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 120, 60, 200)) # ...fading to fully transparent
$g.FillPath($glow, $glowArea)
$framePen = [System.Drawing.Pen]::new($borderColor, 6)
$g.DrawRectangle($framePen, 24, 24, $W - 48, $H - 48)

# 2. The logo, as big as fits a 300-pixel-tall box, keeping its shape.
$logo = [System.Drawing.Image]::FromFile((Join-Path $repo 'branding\voidmod.png'))
$scale = 300 / $logo.Height
$lw = [int]($logo.Width * $scale); $lh = [int]($logo.Height * $scale)
$g.DrawImage($logo, 90, [int](($H - $lh) / 2), $lw, $lh)
$logo.Dispose()

# 3. Text and rows, in a column starting at x = 470.
$x = 470
$titleFont = Get-Font @('Segoe UI Black', 'Arial Black') 104 ([System.Drawing.FontStyle]::Regular)
$subFont   = Get-Font @('Segoe UI Semibold', 'Segoe UI') 34 ([System.Drawing.FontStyle]::Regular)
$rowFont   = Get-Font @('Consolas', 'Lucida Console') 34 ([System.Drawing.FontStyle]::Bold)
$smallFont = Get-Font @('Segoe UI') 24 ([System.Drawing.FontStyle]::Regular)
$g.DrawString('VOIDMODS', $titleFont, [System.Drawing.SolidBrush]::new($white), $x - 8, 120)
$g.DrawString('Mods for FNAF Online Multiplayer', $subFont, [System.Drawing.SolidBrush]::new($purple), $x, 262)

# One row per mod, like the game's menu buttons: box, border, name, a few words, and ">" on the right.
$y = 340
foreach ($m in $mods) {
    $rect = [System.Drawing.Rectangle]::new($x, $y, 700, 76)
    $g.FillRectangle([System.Drawing.SolidBrush]::new($rowColor), $rect)
    $g.DrawRectangle([System.Drawing.Pen]::new($borderColor, 4), $rect)
    $g.DrawString($m.Name, $rowFont, [System.Drawing.SolidBrush]::new($white), $x + 26, $y + 18)
    $nameWidth = $g.MeasureString($m.Name, $rowFont).Width
    $g.DrawString($m.About, $smallFont, [System.Drawing.SolidBrush]::new($dim), $x + 40 + $nameWidth, $y + 25)
    $g.DrawString('>', $rowFont, [System.Drawing.SolidBrush]::new($purple), $x + 650, $y + 16)
    $y += 96
}

$g.Dispose()
$out = Join-Path $repo 'branding\social-preview.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Wrote $out ($W x $H, $([math]::Round((Get-Item $out).Length / 1KB)) KB)"
