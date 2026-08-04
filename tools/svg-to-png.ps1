# Rastérise une icône SVG mono-chemin en PNG.
#
# Passe par WPF plutôt que par un moteur SVG : Geometry.Parse lit la même mini-syntaxe de chemin que
# l'attribut "d" du SVG, et RenderTargetBitmap sait la rendre avec anticrénelage. Aucune dépendance
# à installer, et le résultat est net à n'importe quelle taille — c'est tout l'intérêt de partir du
# vecteur plutôt que d'agrandir un PNG de 64 px.
#
# Limite assumée : un seul <path>. Les jeux Iconify utilisés ici en ont un dans la quasi-totalité
# des cas ; le script le dit plutôt que de rendre l'icône à moitié.
#
# Usage :
#   .\svg-to-png.ps1 -Source "Library\Icons\fluent\window-24-regular.svg" -Output "out.png" [-Size 512] [-Color "#FFFFFFFF"]

param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [int]$Size = 512,
    [string]$Color = "#FFFFFFFF"
)

Add-Type -AssemblyName PresentationCore, WindowsBase

$svg = Get-Content (Resolve-Path $Source) -Raw

$paths = [regex]::Matches($svg, '<path[^>]*\sd="([^"]+)"')
if ($paths.Count -eq 0) { throw "Aucun <path d=...> dans $Source" }
if ($paths.Count -gt 1) { Write-Warning "$($paths.Count) chemins trouvés ; ils seront fusionnés." }

$box = [regex]::Match($svg, 'viewBox="\s*([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)\s+([\d.\-]+)')
if (-not $box.Success) { throw "viewBox absent de $Source" }
$vbX = [double]$box.Groups[1].Value
$vbY = [double]$box.Groups[2].Value
$vbW = [double]$box.Groups[3].Value
$vbH = [double]$box.Groups[4].Value

$scale = $Size / [Math]::Max($vbW, $vbH)

$visual = New-Object System.Windows.Media.DrawingVisual
$dc = $visual.RenderOpen()
$dc.PushTransform((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
$dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$vbX), (-$vbY)))

$brush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($Color))
foreach ($m in $paths) {
    # FillRule nonzero : c'est celle du SVG par défaut, et c'est elle qui creuse correctement les
    # contre-formes d'une icône au trait. Avec evenodd les découpes s'inversent.
    $geometry = [System.Windows.Media.Geometry]::Parse("F1 " + $m.Groups[1].Value)
    $dc.DrawGeometry($brush, $null, $geometry)
}

$dc.Pop(); $dc.Pop(); $dc.Close()

$bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap `
    $Size, $Size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)

$encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [System.IO.File]::Create((New-Item -ItemType File -Path $Output -Force).FullName)
try { $encoder.Save($stream) } finally { $stream.Dispose() }

"rendu : $Output  ${Size}x${Size}  (viewBox ${vbW}x${vbH}, $($paths.Count) chemin(s))"
