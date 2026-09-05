# Icônes — provenance et licences

75 272 icônes, 10 jeux, récupérées depuis [Iconify](https://iconify.design) via le dépôt
[iconify/icon-sets](https://github.com/iconify/icon-sets). Un dossier par jeu, comme sur le site.

**Tous les jeux ci-dessous autorisent l'usage commercial**, donc la distribution dans une version
gratuite comme dans une version payante de SenSÉ. C'est le critère qui a présidé à la sélection :
Iconify agrège 231 jeux, dont certains en *non commercial* et en GPL-2, volontairement écartés.

Chaque dossier contient son propre `LICENSE.txt` avec l'auteur, la source et le texte de référence.

## Ce qui est inclus

### Interface — pour tes différents thèmes

| Dossier | Icônes | Licence | Style |
|---|---:|---|---|
| `fluent` | 20 088 | MIT | Microsoft Fluent. Le plus proche de l'aspect Windows |
| `material-symbols` | 16 284 | Apache 2.0 | Google Material, plusieurs graisses |
| `ph` | 9 161 | MIT | Phosphor, fin et régulier |
| `mdi` | 7 638 | Apache 2.0 | Material Design Icons, le classique |
| `tabler` | 6 232 | MIT | Trait minimal, 24 px |
| `hugeicons` | 5 091 | MIT | Contemporain, contrasté |

Six familles visuellement distinctes : de quoi donner à chaque thème sa propre identité sans changer
de vocabulaire d'icônes.

### Marques et réseaux sociaux

| Dossier | Icônes | Licence |
|---|---:|---|
| `simple-icons` | 3 723 | CC0 1.0 |
| `logos` | 2 091 | CC0 |
| `cib` | 830 | CC0 1.0 |

Domaine public : aucune attribution requise. `simple-icons` est monochrome, `logos` est en couleurs
d'origine.

### Jeu vidéo

| Dossier | Icônes | Licence |
|---|---:|---|
| `game-icons` | 4 134 | CC BY 3.0 |

**Attribution obligatoire.** Voir la section ci-dessous.

## Ce que tu dois faire pour publier

**`game-icons` — CC BY 3.0.** Commercial autorisé, mais l'attribution est une condition de la
licence, pas une politesse. Il faut créditer les auteurs quelque part de visible dans le logiciel :
l'écran À propos convient. Les auteurs sont listés sur <https://game-icons.net>. Si tu préfères
éviter cette contrainte, supprime le dossier — c'est le seul du lot concerné.

**MIT et Apache 2.0** (`fluent`, `material-symbols`, `ph`, `mdi`, `tabler`, `hugeicons`). La mention
de licence doit accompagner la distribution. Les `LICENSE.txt` des dossiers y suffisent s'ils sont
livrés avec ; sinon, reprends-les dans les mentions légales.

**CC0** (`simple-icons`, `logos`, `cib`). Rien à faire.

## Un point qui n'est pas une question de licence

Les icônes de marque sont libres **en tant que fichiers**, mais les marques qu'elles représentent
appartiennent à leurs détenteurs. S'en servir pour désigner un service — un bouton « ouvrir Steam »,
un lien Discord — est de l'usage nominatif, admis. S'en servir comme identité de ton propre logiciel
ne l'est pas. Cela vaut pour le logo Steam en particulier, vu ce que fait SenSÉ.

## Ajouter d'autres jeux

L'outil est dans `tools/IconFetch`. Un jeu par argument :

```
dotnet run --project tools/IconFetch -c Release -- Icons <prefixe> [<prefixe> ...]
```

Les préfixes sont ceux du site. Vérifie la licence du jeu avant de l'ajouter : elle est affichée sur
la page du jeu, et l'outil la recopie dans le `LICENSE.txt` du dossier.

## Icônes utilisées dans les binaires

| Icône | Source | Licence |
|---|---|---|
| `branding/Desktop.ico` (SenSÉ.Desktop.exe) | `fluent/window-24-regular.svg` | MIT — libre en gratuit et en payant, aucune attribution obligatoire |
| `branding/OverlayKeyboard.ico` (SenSÉ.Osk.exe) | `clavier overlay.png` (fourni) | à confirmer par l'auteur du projet |
| `branding/SenSÉ.ico` (SenSÉ.exe) | `background/SenSÉ_share_image1.png` | œuvre du projet |

Aucune icône Icons8 n'est employée : leur palier gratuit impose un lien d'attribution
visible et un produit payant demande une licence achetée, ce qui ne convient pas à un
logiciel distribué en version gratuite comme en version payante.