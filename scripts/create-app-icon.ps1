$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetsDir = "C:\Users\FabioNET\Desktop\git_work_by_warp\DesktopPager\src\DesktopPager.Tray\Assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$iconPath = Join-Path $assetsDir "DesktopPager.ico"

$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(24, 24, 32))

$bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(33, 150, 243))
$g.FillEllipse($bgBrush, 18, 18, 220, 220)

$font = New-Object System.Drawing.Font("Segoe UI", 88, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$g.DrawString("DP", $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, 0, 256, 256)), $sf)

$ms = New-Object System.IO.MemoryStream
$bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$pngBytes = $ms.ToArray()

$fs = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]1)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([Byte]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]32)
$bw.Write([UInt32]$pngBytes.Length)
$bw.Write([UInt32]22)
$bw.Write($pngBytes)
$bw.Close()
$fs.Close()

$g.Dispose()
$bmp.Dispose()
$ms.Dispose()
$bgBrush.Dispose()
$font.Dispose()
$sf.Dispose()

Get-Item $iconPath | Select-Object FullName, Length, LastWriteTime
