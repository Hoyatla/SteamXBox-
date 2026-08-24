# Route Word « mise en page conservée »

Une **seconde** route vers `.docx`, à côté de celle qui existe. Pas un remplacement : le
convertisseur porte déjà deux philosophies opposées selon la cible — la présentation garde les
coordonnées, le document garde le flux — et les deux ont leurs usages. Celle-ci ajoute le choix
« fidèle à la page » pour qui veut lire, pas éditer.

## Le défaut qu'elle corrige

La route en flux insère les images **en ligne**, chacune dans son paragraphe, à l'endroit décidé par
le compte des mots situés au-dessus. Sur un document réel — 37 images et 5 294 caractères sur
7 pages — ça donne quatre images par page posées dans l'ordre de lecture, et un texte haché entre
elles. Mesuré, reproduit, et non résolu par le seuil de taille : celui-ci écarte des pictogrammes, il
ne place rien.

## Les deux mécanismes

**Les images : `Anchor` au lieu d'`Inline`.** Position horizontale et verticale en EMU relatives à la
page, habillage `None`. Les coordonnées existent déjà — c'est le `BoundingBox` que le filtre consulte
aujourd'hui pour décider de garder l'image, puis jette. Il suffit de le convertir : 12 700 EMU par
point.

**Le texte : `w:framePr` sur le paragraphe.** Le mécanisme de cadre de Word — `x`, `y` en twips,
`hAnchor="page"`, `vAnchor="page"` — pose un paragraphe à des coordonnées exactes. Nettement plus
simple que des zones de texte DrawingML, et supporté partout.

## La détection par bloc

Elle est déjà livrée : `UglyToad.PdfPig.DocumentLayoutAnalysis` est dans les binaires du produit. Ses
segmenteurs regroupent les mots en blocs — un titre, un paragraphe, une légende — sans qu'on écrive
cette partie.

C'est **le** point qui décide de la viabilité. Un cadre par mot donnerait des milliers d'objets ; un
cadre par bloc en donne quelques dizaines par page.

## La protection, et pourquoi elle n'est pas optionnelle

La version positionnée a déjà été tentée sur ce projet, puis abandonnée : dix mille objets pour
quatre cents paragraphes, et Word ramait à chaque coup de molette. Le chiffre venait d'un autre
document que celui mesuré ici, mais il reste la borne à respecter.

**Compter avant d'écrire.** Le nombre d'objets — images ancrées plus cadres de texte — est connu
avant la moindre écriture, puisqu'il sort de la segmentation. Au-delà d'un plafond, la conversion
**retombe sur la route en flux** et le dit dans son message de retour.

Un document mal placé vaut mieux qu'un document qui fige Word, et un utilisateur prévenu vaut mieux
qu'un utilisateur qui attend.

Le plafond se mesure, il ne se décrète pas : `SteamXBox.Indexer --diag-pdf <fichier>` donne le nombre
d'images posées et la couche texte ; la segmentation donnera le nombre de blocs. Quelques centaines
d'objets est l'ordre de grandeur de départ, à confirmer sur une machine modeste — c'est elle qui
décide, pas la machine de développement.

## Ce qui existe déjà et se réutilise

| Pièce | Où |
|---|---|
| Lecture des images et de leurs boîtes | `PdfToDocument.Pictures` |
| Conversion points → EMU | `PdfToDocument.PictureParagraph`, 12 700 par point |
| Déduplication des images par empreinte | `PdfToDocument.Deja` |
| Placement à des coordonnées, déjà écrit | `PdfToPresentation` |
| Mesure d'un document | `SteamXBox.Indexer --diag-pdf` |
| Segmentation en blocs | `UglyToad.PdfPig.DocumentLayoutAnalysis`, déjà livré |

## L'ordre à suivre

1. Segmenter une page et **compter** les blocs sur les quatre PDF de test. Sans ce chiffre, le
   plafond est une devinette.
2. Écrire les images ancrées seules, sans toucher au texte : une page dont les images sont bien
   placées et le texte en flux est déjà vérifiable à l'œil.
3. Ajouter les cadres de texte.
4. Poser le plafond et le repli, et vérifier qu'il se déclenche sur un document lourd.
