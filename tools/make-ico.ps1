<#
  make-ico.ps1 - turns a PNG picture into a Windows .ico icon file with several sizes inside.

  HOW TO RUN
    pwsh -NoProfile -File tools\make-ico.ps1 -Png branding\voidmod.png -Ico branding\voidmod.ico
  WHAT IT NEEDS
    PowerShell 7 on Windows (it uses System.Drawing, which comes with it, to resize the picture).

  HOW IT WORKS
    An .ico file is a small "folder" of pictures: Windows picks the size that fits best (16 px in lists, 48 px
    for big icons, 256 px for extra-large). The file layout is:
      header (6 bytes):        0, type 1 (= icon), number of pictures
      one entry per picture:   width, height (0 means 256), 0, 0, planes 1, 32 bits per pixel, byte size, where it starts
      the pictures themselves: each stored as a complete PNG file (allowed since Windows Vista)
    Each size is drawn centered on a transparent square, so the logo keeps its shape.
#>
param(
    [Parameter(Mandatory)][string]$Png,
    [Parameter(Mandatory)][string]$Ico,
    [int[]]$Sizes = @(16, 24, 32, 48, 64, 128, 256)
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Image]::FromFile((Resolve-Path $Png).Path)
$pictures = foreach ($size in $Sizes) {
    # A transparent square canvas of this size, and the logo scaled to fit inside it.
    $bmp = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $scale = [Math]::Min($size / $source.Width, $size / $source.Height)
    $w = [int][Math]::Round($source.Width * $scale); $h = [int][Math]::Round($source.Height * $scale)
    $g.DrawImage($source, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
    $g.Dispose()
    # Save this size as PNG bytes in memory.
    $ms = [IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}
$source.Dispose()

# Write the .ico file. BinaryWriter writes numbers in the little-endian order the .ico format expects.
$out = [IO.MemoryStream]::new()
$bw = [IO.BinaryWriter]::new($out)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$pictures.Count)   # header
$offset = 6 + 16 * $pictures.Count                                              # pictures start after header + entries
foreach ($p in $pictures) {
    $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }                           # 256 is written as 0 (one byte only)
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$p.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $p.Bytes.Length
}
foreach ($p in $pictures) { $bw.Write($p.Bytes) }
$bw.Flush()
[IO.File]::WriteAllBytes($Ico, $out.ToArray())
"Wrote $Ico ($($pictures.Count) sizes: $($Sizes -join ', '); $($out.Length) bytes)"
