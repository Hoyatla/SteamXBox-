# Library

Matière première. Rien ici n'est lu par SteamXBox à l'exécution — c'est la réserve dans laquelle on
puise pour fabriquer ce qui, lui, est livré.

C'est la distinction qui justifie ce dossier : la racine ne doit contenir que ce qui tourne ou ce
qui sert à construire. Tout le reste encombrait la vue.

| Dossier | Contenu | Destination |
|---|---|---|
| `Icons/` | 75 000 icônes Iconify, 10 jeux | source des `.ico` et des glyphes — voir `tools/svg-to-png.ps1` |
| `Cursors/` | paquets de curseurs décompressés | source des plugins `theme.windows` |
| `background/` | logos, affiches, fonds | source des icônes d'application |
| `branding/` | `.ico` fabriqués | copiés dans `src/` au moment de la construction |
| `Keyboard layout/` | dispositions clavier de référence | conception du clavier overlay |
| `Exclusif/` | éditions thématiques | maquettes à débattre, pas des livrables |
| `design/` | notes de conception | l'origine de la feuille de route |
| `archive/` | binaires et fichiers remplacés | conservés, jamais supprimés |

## Ce qui n'est pas ici, et pourquoi

`Themes/` et `Plugins/` sont restés à la racine : ils sont **lus à l'exécution**, résolus à partir du
dossier de l'exécutable. Les déplacer casserait le chargement du thème et des curseurs.

## Licences

Chaque ressource tierce porte sa licence dans son dossier. `Icons/LICENCES.md` couvre les dix jeux
et recense les icônes réellement employées dans les binaires. Les paquets de curseurs gardent leur
`readme.txt` d'origine.

Ce n'est pas de la bureaucratie : SteamXBox est distribué en version gratuite **et** payante, et une
ressource dont la licence interdit l'usage commercial ne peut pas être livrée avec. Une ressource
sans licence déclarée est à considérer comme inutilisable, pas comme libre.
