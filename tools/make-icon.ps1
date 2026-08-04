# Fabrique un .ico multi-tailles a partir d'un PNG.
#
# Ecrit le conteneur ICO a la main plutot que d'utiliser Icon.Save : System.Drawing ne sait ecrire
# qu'une seule taille, et Windows choisit l'image selon le contexte (16 px dans la barre des taches,
# 256 px dans l'explorateur en grandes icones). Un .ico mono-taille est mis a l'echelle par le
# systeme et devient flou partout sauf a sa taille native.
#
# Les entrees sont des PNG compresses, format accepte depuis Vista, ce qui evite le masque AND
# monochrome du format BMP historique et preserve la transparence.
#
# Usage :
#   .\make-icon.ps1 -Source "Library\background\logo.png" -Output "chemin\SteamXBox.ico" `
#                   [-CropX 0 -CropY 0 -CropSize 0] [-Padding 0.06]

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [int]$CropX = 0,
    [int]$CropY = 0,
    [int]$CropW = 0,
    [int]$CropH = 0,
    [double]$Padding = 0.0,
    [string]$Preview = "",
    # Recolore les pixels opaques (ex. "#FFFFFF"). Un dessin monochrome noir sur transparent
    # disparait sur une barre des taches sombre ; le teinter le rend visible.
    [string]$Tint = "",
    # Pastille de fond, coins arrondis (ex. "#1E1E28"). Sans elle, un glyphe blanc serait invisible
    # sur un fond clair : la pastille garantit le contraste des deux cotes.
    [string]$Background = ""
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 24, 32, 48, 64, 128, 256)

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source))
try {
    if ($CropW -le 0) { $CropW = $src.Width - $CropX }
    if ($CropH -le 0) { $CropH = $src.Height - $CropY }

    $frames = @()
    foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        if ($Background) {
            # Rayon proportionnel : une pastille dont le rayon serait fixe parait carree a 256 px et
            # ronde a 16 px.
            $r = [Math]::Max(2, [int]($size * 0.18))
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $path.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
            $path.AddArc($size - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
            $path.AddArc($size - 2 * $r, $size - 2 * $r, 2 * $r, 2 * $r, 0, 90)
            $path.AddArc(0, $size - 2 * $r, 2 * $r, 2 * $r, 90, 90)
            $path.CloseFigure()
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($Background))
            $g.FillPath($brush, $path)
            $brush.Dispose()

            # Le dessin est ensuite limite a la pastille. Sans ce detourage, une source au fond
            # opaque — un logo noir sur blanc, par exemple — repeindrait les coins arrondis et
            # l'icone redeviendrait un carre.
            $g.SetClip($path)
            $path.Dispose()
        }

        # La zone source garde ses proportions et se pose au centre du carre. Un logo plus large que
        # haut — un rond au-dessus d'un nom, par exemple — serait ampute par un recadrage carre :
        # c'est le nom qu'on perdrait, puisqu'il est en bas.
        $inset = [int]($size * $Padding)
        $box = $size - 2 * $inset
        $scale = [Math]::Min($box / [double]$CropW, $box / [double]$CropH)
        $w = [int][Math]::Round($CropW * $scale)
        $h = [int][Math]::Round($CropH * $scale)
        $dest = New-Object System.Drawing.Rectangle ([int](($size - $w) / 2)), ([int](($size - $h) / 2)), $w, $h
        $from = New-Object System.Drawing.Rectangle $CropX, $CropY, $CropW, $CropH

        if ($Tint) {
            # Remplace les canaux R/V/B en gardant l'alpha : la forme et son anticrenelage sont
            # preserves, seule la couleur change. Une matrice de couleur fait ca en une passe, sans
            # parcourir les pixels.
            $c = [System.Drawing.ColorTranslator]::FromHtml($Tint)
            $m = New-Object System.Drawing.Imaging.ColorMatrix
            $m.Matrix00 = 0; $m.Matrix11 = 0; $m.Matrix22 = 0; $m.Matrix33 = 1
            $m.Matrix40 = $c.R / 255.0; $m.Matrix41 = $c.G / 255.0; $m.Matrix42 = $c.B / 255.0
            $attr = New-Object System.Drawing.Imaging.ImageAttributes
            $attr.SetColorMatrix($m)
            $g.DrawImage($src, $dest, $from.X, $from.Y, $from.Width, $from.Height,
                         [System.Drawing.GraphicsUnit]::Pixel, $attr)
            $attr.Dispose()
        } else {
            $g.DrawImage($src, $dest, $from, [System.Drawing.GraphicsUnit]::Pixel)
        }

        $g.Dispose()

        if ($Preview -and $size -eq 256) {
            $bmp.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
        }

        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
        $ms.Dispose()
    }
} finally {
    $src.Dispose()
}

$out = [System.IO.File]::Create((New-Item -ItemType File -Path $Output -Force).FullName)
try {
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([UInt16]0)                    # reserve
    $w.Write([UInt16]1)                    # type : icone
    $w.Write([UInt16]$frames.Count)

    # Les donnees suivent le repertoire : 6 octets d'en-tete + 16 par entree.
    $offset = 6 + (16 * $frames.Count)
    foreach ($f in $frames) {
        # 256 s'ecrit 0 sur un octet : c'est la convention du format.
        $w.Write([Byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
        $w.Write([Byte]$(if ($f.Size -ge 256) { 0 } else { $f.Size }))
        $w.Write([Byte]0)                  # palette
        $w.Write([Byte]0)                  # reserve
        $w.Write([UInt16]1)                # plans
        $w.Write([UInt16]32)               # bits par pixel
        $w.Write([UInt32]$f.Bytes.Length)
        $w.Write([UInt32]$offset)
        $offset += $f.Bytes.Length
    }
    foreach ($f in $frames) { $w.Write($f.Bytes) }
    $w.Flush()
} finally {
    $out.Dispose()
}

$info = Get-Item $Output
"ecrit : $($info.FullName)  $([int]($info.Length / 1KB)) Ko  tailles: $($sizes -join ', ')"
