<#
    Publication single-file du binaire SenSÉ.Desktop.

    Le 09 septembre 2026, le pipeline standard
      dotnet publish -c Release -p:PublishSingleFile=true
    a commence a produire un bundle bit-identique a la publication precedente, malgre les
    modifications du source. Le bundle ecrit en sortie ne contient que des stubs d assemblies
    (4 Ko) au lieu des assemblies reelles, et les BAML compiles depuis les .xaml en sous-
    dossiers (SenSÉ.Shell\Themes\DarkTheme.xaml, SenSÉ.Desktop\ControlCentre\PluginIcons.xaml)
    sont absents. Le runtime leve alors XamlParseException au demarrage de App.xaml, et les
    icones VectorGeometry ajoutees dans PluginIcons.xaml n apparaissent pas.

    La parade est de separer les deux phases :
      1. dotnet build direct, qui produit les vrais BAML et les assemblies completes.
      2. dotnet publish --no-build, qui se contente de packager ce qui est deja sur disque.
    Avec --no-build, la phase de publish ne rebuilde pas, et n ecrase donc pas le travail
    du build direct avec ses stubs.

    Ce script encapsule la sequence. Il prend le chemin de destination en parametre (par defaut
    C:\Program Files\SenSÉ, qui est l installation de reference) et refuse de continuer si
    la publication ne change pas le SHA256 du binaire : c est l indice que le bug single-file
    est de retour et qu il faut nettoyer le cache ou les obj/ avant de relancer.
#>
[CmdletBinding()]
param(
    [string]$Destination = "C:\Program Files\SenSÉ",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

# La racine du repo est le parent du dossier tools/ ou vit ce script.
$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot ("src" + [char]92 + "SenSÉ.Desktop" + [char]92 + "SenSÉ.Desktop.csproj")
$shellCsproj = Join-Path $repoRoot ("src" + [char]92 + "SenSÉ.Shell" + [char]92 + "SenSÉ.Shell.csproj")
$exe = Join-Path $Destination "SenSÉ.Desktop.exe"
$bamlShell = Join-Path $repoRoot ("src" + [char]92 + "SenSÉ.Shell" + [char]92 + "obj" + [char]92 + $Configuration + [char]92 + "net10.0-windows" + [char]92 + "Themes" + [char]92 + "DarkTheme.baml")
$bamlDesktop = Join-Path $repoRoot ("src" + [char]92 + "SenSÉ.Desktop" + [char]92 + "obj" + [char]92 + $Configuration + [char]92 + "net10.0-windows" + [char]92 + "win-x64" + [char]92 + "ControlCentre" + [char]92 + "PluginIcons.baml")

# Capture l etat du binaire de destination s il existe deja, pour verifier qu on
# produit bien un bundle different a la fin.
$previousSha = $null
if (Test-Path $exe) {
    $previousSha = (Get-FileHash $exe -Algorithm SHA256).Hash
}

# 1) Clean des deux projets dont la sequence de build est connue pour produire
# les BAML dans les sous-dossiers.
Write-Host "=== Clean ===" -ForegroundColor Cyan
dotnet clean $shellCsproj -c $Configuration --nologo | Out-Null
dotnet clean $csproj -c $Configuration --nologo | Out-Null

# 2) Build direct : c est l etape qui produit les vrais BAML et les assemblies completes.
# Si on la saute (publish qui rebuild), le bundle final ne contient que des stubs 4 Ko.
Write-Host "=== Build ($Configuration) ===" -ForegroundColor Cyan
dotnet build $csproj -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build SenSÉ.Desktop a échoué (code $LASTEXITCODE)." }

# 3) Sanity check : les BAML qui manquent dans le bundle cassent sont-ils presents ?
if (-not (Test-Path $bamlShell)) {
    throw "Build direct n a pas produit DarkTheme.baml dans obj" + [char]92 + "Release. Le bug .NET 10 single-file est de retour, ou le SDK a change. Consulter docs/build.md."
}
if (-not (Test-Path $bamlDesktop)) {
    throw "Build direct n a pas produit ControlCentre" + [char]92 + "PluginIcons.baml dans obj" + [char]92 + "Release. Consulter docs/build.md."
}
Write-Host "BAML OK : DarkTheme + PluginIcons" -ForegroundColor DarkGreen

# 4) Publish --no-build : la phase de bundling utilise les assemblies deja compilees.
Write-Host "=== Publish (--no-build) ===" -ForegroundColor Cyan
dotnet publish $csproj -c $Configuration --no-build -r $Runtime --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -o $Destination --nologo
if ($LASTEXITCODE -ne 0) { throw "Publish a échoué (code $LASTEXITCODE)." }

# 5) Verifications finales sur le bundle.
if (-not (Test-Path $exe)) { throw "Binaire non produit : $exe" }
$size = (Get-Item $exe).Length
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash

# Verification de contenu : si le bug single-file est de retour, le bundle
# ne contient pas les strings IconFlow / IconPenSparkle / DarkTheme.
# Le SHA peut rester identique si on republie un bundle deja bon, c est OK.
foreach ($needle in @("IconFlow", "IconPenSparkle", "DarkTheme")) {
    if (-not (Select-String -Path $exe -Pattern ([regex]::Escape($needle)) -Quiet)) {
        throw ("Le bundle ne contient pas la string {0}. Le bug single-file est de retour : vider %LOCALAPPDATA%" + [char]92 + "Temp" + [char]92 + ".net, supprimer obj/Release et bin/Release de SenSÉ.Shell et SenSÉ.Desktop, puis relancer.") -f $needle
    }
}

Write-Host ""
Write-Host "OK : $exe" -ForegroundColor Green
Write-Host ("Taille : {0} octets ({1} Mo)" -f $size, [math]::Round($size/1MB, 2))
Write-Host ("SHA256 : {0}" -f $sha)
if ($previousSha) {
    if ($previousSha -eq $sha) {
        Write-Host ("Bundle inchange depuis la derniere publication ({0})" -f $previousSha) -ForegroundColor DarkGray
    } else {
        Write-Host ("Bundle modifie.")
        Write-Host ("  Avant : {0}" -f $previousSha) -ForegroundColor DarkGray
    }
}
Write-Host ""
