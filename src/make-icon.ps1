Add-Type -AssemblyName System.Drawing

$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# rounded black square
$r = New-Object System.Drawing.RectangleF 16, 16, 224, 224
$d = 48
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
$path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
$path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
$path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
$path.CloseFigure()

$fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 20, 20, 20))
$g.FillPath($fill, $path)
$border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 120, 120, 120)), 4
$g.DrawPath($border, $path)

# white X
$xpen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 26
$xpen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$xpen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($xpen, 84, 84, 172, 172)
$g.DrawLine($xpen, 84, 172, 172, 84)

$g.Dispose()

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
$png = $ms.ToArray()
$ms.Dispose()

$out = Join-Path $PSScriptRoot "icon.ico"
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0)      # reserved
$bw.Write([uint16]1)      # type: icon
$bw.Write([uint16]1)      # count
$bw.Write([byte]0)        # width 256
$bw.Write([byte]0)        # height 256
$bw.Write([byte]0)        # palette
$bw.Write([byte]0)        # reserved
$bw.Write([uint16]1)      # planes
$bw.Write([uint16]32)     # bpp
$bw.Write([uint32]$png.Length)
$bw.Write([uint32]22)     # offset
$bw.Write($png)
$bw.Close()
$fs.Close()

Write-Output ("icon written: " + $out + " (" + $png.Length + " bytes png)")
