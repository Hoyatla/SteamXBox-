# Publication SenSÉ.Desktop : build + publish en 2 etapes

## Le bug

`dotnet publish -c Release -p:PublishSingleFile=true` casse la compilation des BAML des .xaml qui vivent dans des sous-dossiers. Symptomes :

- Le bundle final `SenSÉ.Desktop.exe` contient des stubs 4 Ko a la place des assemblies reelles (SenSÉ.Shell.dll, SenSÉ.Desktop.dll, etc.).
- Les BAML `SenSÉ.Shell\Themes\DarkTheme.baml` et `SenSÉ.Desktop\ControlCentre\PluginIcons.baml` ne sont pas produits dans `obj\Release\`.
- Au demarrage, `App.xaml` leve `XamlParseException` : `Impossible de trouver la ressource 'themes/darktheme.xaml'`.
- Les `<Geometry x:Key="...">` ajoutes dans `PluginIcons.xaml` n'apparaissent pas dans la grille de tuiles (fallback sur le glyph).
- Le bundle est bit-identique d'une publication a l'autre, meme apres `dotnet clean`, suppression manuelle d'obj/bin et du cache `%LOCALAPPDATA%\Temp\.net` (908 Mo). SHA256 inchange : `e762f731ed74c40e...`.

Cause probable : `PublishSingleFile=true` reutilise un bundle mis en cache quelque part dans le SDK que ni `dotnet clean` ni la suppression d'obj/bin ne flushent. La phase de publish reecrit les assemblies de `bin\Release\` par des stubs, et le bundle final est tire de ce cache au lieu des assemblies fraichement compilees.

## Le fix procedural

Separer les deux phases en utilisant `--no-build` sur le publish :

```powershell
# Etape 1 : build direct, qui produit les vrais BAML et les assemblies completes.
dotnet build src\SenSÉ.Desktop\SenSÉ.Desktop.csproj -c Release

# Etape 2 : publish --no-build, qui se contente de packager les assemblies deja compilees.
dotnet publish src\SenSÉ.Desktop\SenSÉ.Desktop.csproj -c Release --no-build 
    -r win-x64 --self-contained true 
    -p:PublishSingleFile=true -p:PublishReadyToRun=false 
    -o 'C:\Program Files\SenSÉ'
```

Le build direct produit les vrais BAML (la SDK auto-inclut bien les .xaml des sous-dossiers en Release, ce que `NETSDK1022` confirme si on declare `<Page>` en double). Le publish avec `--no-build` reutilise ces assemblies au lieu de rebuilder et d'ecraser avec des stubs.

## Le script

`tools\publish-desktop.ps1` wrap la sequence en 2 etapes avec les verifications qui vont bien :

```powershell
.\tools\publish-desktop.ps1
# ou avec une autre destination :
.\tools\publish-desktop.ps1 -Destination 'D:\staging'
```

Le script :

1. Capture le SHA256 du bundle existant (s'il y en a un) pour verifier plus tard qu'il change.
2. Fait `dotnet clean` sur SenSÉ.Shell et SenSÉ.Desktop.
3. Fait `dotnet build` direct de SenSÉ.Desktop (qui produit les vrais BAML).
4. Verifie que `DarkTheme.baml` et `PluginIcons.baml` sont presents dans `obj\Release\`.
5. Fait `dotnet publish --no-build` avec les flags single-file.
6. Verifie que le bundle existe, capture taille et SHA256, et refuse de continuer si le SHA est identique au precedent (indice que le bug single-file est de retour).

## Caveats

- Le cache `%LOCALAPPDATA%\Temp\.net` peut etre flushe si le bundle redevient bit-identique (symptome d'un retour du bug).
- `dotnet clean` tout seul ne suffit pas, contrairement a un build + publish classique : c'est la combinaison `clean` + `build` + `publish --no-build` qui force le bon ordre.
- Pour les autres binaires (Atelier, Editeur Texte, etc.), le meme probleme peut survenir. Le script peut etre adapte en changeant la csproj et l'exe cible.
