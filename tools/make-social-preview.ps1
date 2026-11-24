<#
  make-social-preview.ps1 - draws the GitHub "Social Preview" banner for VOIDMODS (1280 x 640, the size GitHub asks for).

  HOW TO RUN (PowerShell 7, from the repo folder):   pwsh -NoProfile -File tools\make-social-preview.ps1
  WHAT IT NEEDS: PowerShell 7 on Windows (System.Drawing, Windows' drawing library) and branding\voidmod.png.
  WHAT IT MAKES: branding\social-preview.png. Upload it on github.com: repo Settings -> General -> Social preview.
  It names no single mod on purpose, so it stays right when new VOIDMODS are added.

  HOW IT DRAWS (like painting layers from back to front):
    1. background: near-black purple, with a soft purple glow behind the logo (a "path gradient": a color that
       fades out from a center point), and a frame like the game's menu box
    2. the V logo on the left, scaled up with high-quality smoothing
    3. "VOIDMODS", the subtitle, a thin purple line, and a small credit line
    GitHub crops nothing, but shows the banner small in link previews, so everything important stays big and
    at least 40 pixels away from the edges.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$repo = Split-Path -Parent $PSScriptRoot
$W = 1280; $H = 640

# Colors (the same purples as UiKit.cs in ModConfigs). FromArgb(alpha, red, green, blue), each 0-255.
$bgColor     = [System.Drawing.Color]::FromArgb(255, 9, 5, 15)
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

# 3. Text, in a column starting at x = 470, centered as a block on the logo's middle (y = 320).
$x = 470
$titleFont = Get-Font @('Segoe UI Black', 'Arial Black') 120 ([System.Drawing.FontStyle]::Regular)
$subFont   = Get-Font @('Segoe UI Semibold', 'Segoe UI') 40 ([System.Drawing.FontStyle]::Regular)
$smallFont = Get-Font @('Segoe UI') 28 ([System.Drawing.FontStyle]::Regular)
$g.DrawString('VOIDMODS', $titleFont, [System.Drawing.SolidBrush]::new($white), $x - 10, 150)
$g.DrawString('Mods for FNAF Online Multiplayer', $subFont, [System.Drawing.SolidBrush]::new($purple), $x, 315)
# A thin purple line between the subtitle and the credit, like the lines around "Multiplayer" in the game's title.
$g.FillRectangle([System.Drawing.SolidBrush]::new($borderColor), $x + 2, 385, 640, 4)
$g.DrawString('MelonLoader mods by Voidmaster01', $smallFont, [System.Drawing.SolidBrush]::new($dim), $x, 405)

$g.Dispose()
$out = Join-Path $repo 'branding\social-preview.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Wrote $out ($W x $H, $([math]::Round((Get-Item $out).Length / 1KB)) KB)"
