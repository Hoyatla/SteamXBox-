# Les outils de SteamXBox

Référence destinée à l'assistant local. Écrite courte et factuelle : elle doit tenir
dans un contexte réduit, donc chaque section dit ce que l'outil fait, ce qu'il exige,
et ce qu'il **refuse** — cette dernière colonne évite au modèle de promettre l'impossible.

Les chiffres sont mesurés sur la machine de développement (RTX 4070 SUPER), pas estimés.

---

## Ce qu'un outil est

Un dossier dans `Plugins/`, contenant un `plugin.json`. Déposé, il est installé ;
supprimé, il est désinstallé. Le manifeste **décrit** — il ne contient pas de code et ne
peut rien faire que l'hôte ne sache déjà faire. La liste des verbes disponibles est dans
`PluginActions` ; les briques d'interface dans `PluginVocabulary`.

Les valeurs des réglages sont passées au verbe dans l'ordre déclaré, séparées par `|`.

---

## Outils livrés

### agrandir-video — Agrandir une vidéo

| | |
|---|---|
| Verbe | `video` |
| Réglages | modèle, agrandissement (2/3/4), cadence (curseur 0–60), qualité (18/23/28), format |
| Sortie | `<nom>_out.<format>` à côté de la source |
| Exige | les deux moteurs livrés, plus **ffmpeg** |

Cadence : `0` conserve celle du film. En dessous, les images en trop sont jetées **avant**
tout calcul — moitié moins d'images, moitié moins de temps. Au-dessus, les images
manquantes sont fabriquées par interpolation, ce qui **triple** environ la durée.
Une valeur à moins de 1 % de la cadence du film est traitée comme « identique » : sans
cette tolérance, choisir 24 sur un film en 23,976 décalerait le son de quatre secondes
sur une heure et demie.

Débit mesuré : **10,7 images/s** en 1280×720 vers 2560×1440, soit environ trente-cinq
minutes par quart d'heure de film. Un film de 80 minutes demande à peu près 3 h 45.

Refuse : `webm` en sortie — ce conteneur ne porte que VP8, VP9 et AV1, jamais du H.264.
La demande est ramenée à `mp4` au lieu d'échouer.

Le travail se fait par tronçons de trente secondes, effacés au fur et à mesure. Sans
cela, un film de 80 minutes réclamerait **572 Go** d'images intermédiaires.

### agrandir-image — Agrandir une image

| | |
|---|---|
| Verbe | `image` |
| Réglages | modèle, agrandissement (2/3/4), format (auto/png/jpg/webp/bmp/tiff) |
| Sortie | `<nom>_out.<format>` à côté de la source |
| Exige | le moteur d'agrandissement ; **ffmpeg seulement** si une conversion est nécessaire |

Entrées acceptées : png, jpg, jpeg, jfif, webp, bmp, tif, tiff, avif. Le moteur ne lit
nativement que jpg, png et webp — les autres sont convertis en PNG avant et reconvertis
après. Agrandir un png en png ne réclame donc rien d'autre que le moteur.

Refuse : **HEIC**. ffmpeg n'a pas de démultiplexeur pour ce conteneur ; l'extension a été
retirée de la liste plutôt que proposée en vain.

### convertir-document — Convertir un document

| | |
|---|---|
| Verbe | `convert` |
| Sortie | à côté du document |
| Exige | LibreOffice, et pour la reconnaissance de texte : tesseract et poppler |

Trois routes selon le format demandé : conversion classique, reconnaissance de caractères
sur les PDF dont les pages sont des images, et rendu de page en image avec une couche de
texte invisible par-dessus.

### comfyui — Génération d'images

Voir plus bas : ComfyUI est sous GPL v3 et n'est jamais livré avec le produit.

---

## Moteurs livrés avec le produit

| Moteur | Emplacement | Licence | Rôle |
|---|---|---|---|
| Real-ESRGAN ncnn | `Outils\Real-ESRGAN-ncnn\` | BSD 3-Clause | agrandissement, images et vidéo |
| RIFE ncnn | `Outils\rife-ncnn-vulkan\` | MIT | interpolation, fabrique les images manquantes |

Compilés, autonomes, 70 Mo à eux deux. Ils n'exigent ni Python ni PyTorch. Modèles
d'agrandissement disponibles : `realesr-animevideov3` en x2, x3 et x4 (rapide, fait pour
la vidéo), `realesrgan-x4plus` et `realesrgan-x4plus-anime` (plus fins, **x4 uniquement** —
l'échelle est ajustée d'elle-même si une autre est demandée).

Faits établis par mesure, utiles pour ne pas refaire les mêmes essais :

- ncnn est **trois fois plus rapide** que la voie PyTorch : 28 s contre 84 s pour 300 images.
- NVENC est **plus lent** que l'encodage processeur ici — 94 s contre 65 s — car le circuit
  d'encodage se dispute la carte avec le calcul. Ne pas y revenir.
- L'interpolateur restitue les images d'origine **au pixel près** ; il n'invente que
  l'entre-deux.

---

## Programmes détectés, jamais livrés

Chacun est déclaré par un manifeste de catégorie `dependency`, dans `Plugins/dependance-*`.
Ce n'est pas un outil : ni tuile, ni panneau, ni action. Il dit seulement **où on cherche
le programme** (`target`, où `{tools}` est résolu par l'hôte) et **où on l'obtient**
(`source`). SteamXBox regarde et le dit dans Paramètres › Outils : présent, ou absent avec
l'adresse. L'ordre de recherche est celui du produit — `Outils` d'abord, le PATH ensuite.


| Programme | Pourquoi pas livré | Sans lui |
|---|---|---|
| ffmpeg | compilation courante en GPL (`--enable-gpl`, ce qui apporte libx264) | l'outil vidéo s'arrête et le dit |
| LibreOffice | MPL, maintenance à l'utilisateur | conversion de documents éteinte |
| tesseract | Apache 2.0, maintenance à l'utilisateur | reconnaissance de texte éteinte |
| poppler | **GPL** | rendu de page PDF éteint |
| ComfyUI | **GPL v3** | génération d'images éteinte |

ffmpeg est cherché dans `Outils\ffmpeg\bin` **avant** le PATH : le poser là évite de
toucher aux réglages de Windows et fige une version connue.

Une compilation LGPL de ffmpeg serait livrable, mais elle n'a justement pas libx264 : il
faudrait encoder par le matériel, ce qui exclurait les machines sans carte compatible.
Le compromis n'existe pas — d'où la détection.

---

## Ce qui n'est plus vrai

À jour au moment où ces lignes sont écrites, pour éviter de rejouer d'anciennes recettes :

- L'agrandissement d'images **ne passe plus par Python ni PyTorch**. `Outils\Real-ESRGAN`
  (version PyTorch) et `Outils\Python` ne servent plus qu'à ComfyUI.
- Le pilote de la chaîne vidéo n'est plus un script : c'est
  `src/SteamXBox.Tools/Video/VideoUpscale.cs`.
- L'interpolation d'images est dans le produit ; le flux ComfyUI
  « Video Frame Interpolation » est redondant.

---

## L'assistant local

Un modèle de langage servi depuis le dossier du produit, qui appelle les outils au lieu
d'expliquer comment les utiliser.

| | |
|---|---|
| Serveur | `Outils\llama.cpp\llama-server.exe`, Vulkan, 102 Mo |
| Modèle | `Outils\Modeles\*.gguf` — le premier trouvé, hors `mmproj-` |
| Adresse | `http://127.0.0.1:8081`, dialecte OpenAI |
| Code | `src/SteamXBox.Tools/Assistant/` (le cœur) et `src/SteamXBox.Desktop/Assistant/` (le panneau) |
| Tuile | `Plugins/assistant`, verbe `assistant` |

Rien n'est installé sur la machine : le produit se déplace avec son assistant.

**Le modèle est téléchargé, jamais livré** — plusieurs gigaoctets qui vieillissent vite, et
la taille qui convient dépend de la machine. L'installeur propose trois choix, tous sous
Apache 2.0 relevée à la source :

| Modèle | Poids | Mémoire | Débit | Pour qui |
|---|---|---|---|---|
| Qwen3.5-9B | 5,3 + 0,9 Go | 6,9 Go | 7,6 j/s | machine de création : génération d'images, audio, 3D, édition de plugins |
| Qwen3.5-4B | 2,6 + 0,6 Go | 3,8 Go | 11,9 j/s | poste de bureau : deux fois plus léger, 56 % plus rapide |

Mesuré sur la machine de développement — vingt-huit cœurs, processeur seul, contexte de
32768. Les deux voient les images, les deux sont sous Apache 2.0, et les deux ont réussi
l'épreuve d'appel d'outils : **le 4B n'est pas un modèle au rabais**, c'est le même travail
plus vite sur un catalogue d'outils plus court.

En changer ne demande aucune modification : déposer un autre `.gguf` dans `Outils\Modeles`
et descendre l'ancien dans `Outils\Modeles\autres\`.

**Un seul modèle vit à la racine du dossier.** La recherche n'entre pas dans les
sous-dossiers, donc `autres\` est ignoré sans qu'aucune règle ne l'énonce : c'est là qu'on
range celui dont on ne se sert plus, pour le reprendre en le remontant. Départager deux
modèles à la racine par l'ordre alphabétique reviendrait à faire dépendre le choix de la
ponctuation d'un nom de fichier — entre `Qwen3.5-4B` et `Qwen3VL-8B`, c'est le point qui
l'emporte sur la lettre, et personne ne le devinerait.

**Le projecteur d'images suit son modèle.** Un fichier `mmproj-*.gguf` n'est pas un modèle :
c'est ce qui lui apprend à regarder, et il n'appartient qu'à lui. Il est apparié sur le plus
long début commun, jamais pris au premier de la liste — un modèle chargé avec les yeux d'un
autre démarre normalement et décrit des images qu'il ne voit pas.

**La liste des outils vient des manifestes**, jamais d'une seconde description. Chaque
outil installé devient une fonction appelable, avec ses réglages tels que le manifeste les
déclare — un choix devient une énumération, un curseur un entier borné, un fichier un
chemin. Ajouter un outil suffit à le rendre disponible pour l'assistant ; il n'y a rien à
écrire ailleurs.

**Le modèle ne reçoit aucun pouvoir nouveau.** Il ne peut nommer que les outils installés,
avec leurs réglages déclarés — exactement ce qu'un utilisateur pourrait cliquer. Il ne
compose pas de commande et ne touche pas au disque : l'hôte exécute.

Vérifié de bout en bout : « Agrandis l'image C:/photos/chat.png en quatre fois, en jpg »
produit l'appel `agrandir_image` avec les quatre réglages remplis, dont les valeurs par
défaut pour ceux que la phrase ne précise pas.

À savoir sur le modèle installé : il **raisonne avant de répondre**, dans un champ séparé.
Cette réflexion est écartée de l'historique — elle ne sert pas la suite et coûte du
contexte. Et il lui arrive d'épuiser sa marge en réfléchissant : quand sa réponse finale
revient vide après l'exécution d'un outil, c'est le résultat de l'outil qui fait réponse.
