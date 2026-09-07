# Plan de développement — le moteur de l'Atelier

**But : l'Atelier remplace ComfyUI.** Il exécute lui-même les modèles de diffusion,
au lieu de les sous-traiter à un serveur Python étranger.

Ce document est écrit après mesure sur la machine du 7 septembre 2026. Tous les
chiffres qu'il contient ont été relevés, aucun n'est estimé.

---

## 1. Ce qui existe déjà

L'Atelier n'est pas une coquille. Ce qui suit fonctionne aujourd'hui :

| Pièce | Fichier | Taille |
|---|---|---|
| Moteur de graphe | `Execution/Moteur.cs` | 222 lignes |
| Contexte d'exécution | `Execution/ContexteExecution.cs` | 38 lignes |
| Catalogue de nœuds | `Bibliotheque/CatalogueNoeuds.cs` | 54 lignes |
| Nœuds Codage | `Bibliotheque/Codage/{Llm,Mcp,Primitives}.cs` | 626 lignes |
| Serveur MCP | `Mcp/ServeurHttp.cs`, `Mcp/Verbes.cs` | `executer`, `executer_noeud`, `annuler`, `refaire` |
| GUI, sérialisation, undo/redo, nœuds custom | `UI/`, `Serialisation/`, `Custom/` | réels |

**Les deux grands groupes existent déjà** dans `Modele/Espace.cs` :

```csharp
public enum Espace { Codage, Multimedia }
```

Ils sont déjà portés par les verbes MCP (paramètre `espace`). Rien à créer de ce
côté : il faut les remplir.

---

## 2. Ce qui manque — une seule chose

`Bibliotheque/Multimedia/Tous.cs` (255 lignes) **est un client HTTP ComfyUI**.
`Mcp/ClientComfyui.cs` poste un graphe Comfy — `class_type`, `CLIPTextEncode` —
sur `127.0.0.1:8188` :

```csharp
"texte_vers_image" → new ClientComfyui().TexteVersImageAsync(prompt, modele)
"image_vers_video" → ResultatExecution.Fail("non implemente MVP - necessite workflow ComfyUI img2vid")
```

L'Atelier ne calcule pas d'image. **Il demande à ComfyUI de le faire.** C'est le
seul endroit à réécrire.

**Conséquence pour le shell SenSÉ :** un serveur Python étranger, avec son
vocabulaire de graphes, ne parlera jamais commandes SenSÉ. Tant que Comfy est
dessous, la génération d'image reste hors de portée du shell. C'est la vraie
raison de ce chantier — pas l'esthétique.

---

## 3. Le remplaçant : `stable-diffusion.cpp`

Le pendant exact de `llama.cpp`, que SenSÉ embarque déjà pour le texte. Un binaire
natif autonome, pas un écosystème.

Vérifié le 7 septembre 2026, build `master-849-d04e895`, CUDA 12, sur RTX 4070
SUPER (12 281 Mio) :

### Flux — fonctionne

```
flux1-schnell-Q5_K_S.gguf + clip_l + t5xxl_fp8 + ae.safetensors
512×512, 4 étapes, --diffusion-fa
→ 16,45 s, image correcte
```

### Wan 2.2 — fonctionne

```
Wan2.2-T2V-A14B-{High,Low}Noise-Q4_K_M.gguf
+ umt5-xxl-encoder-Q4_K_M.gguf + wan_2.1_vae.safetensors
+ LoRA lightx2v 4 étapes
480×480×17 images, 2+2 étapes
→ 63,21 s, vidéo MJPEG 16 i/s, 17 images
```

Le binaire connaît nativement l'architecture à double expert de Wan 2.2 :
`--high-noise-diffusion-model`, `--high-noise-steps`, `--moe-boundary`,
`--vae-format wan`, `-M vid_gen`. Rien à bricoler.

**Il fournit aussi `sd-server.exe`** — la même forme que `llama-server.exe` : un
processus qui vit, garde ses poids chargés, et qu'on arrête avec la fenêtre.

### Le contrat du serveur, relevé

Démarré avec `--listen-port 1234` (défaut `127.0.0.1:1234`), Flux chargé :

| Route | Réponse |
|---|---|
| `GET /v1/models` | `{"data":[{"id":"sd-cpp-local","object":"model","owned_by":"local"}]}` |
| `POST /v1/images/generations` | `{"created":…,"data":[{"b64_json":"…"}]}` |
| `POST /v1/images/edits` | existe (400 sans corps valide) |
| `POST /v1/video/generations` | **404 — n'existe pas** |

C'est une API façon OpenAI. **Le modèle se fixe au lancement du serveur** : les
poids sont passés en arguments de démarrage, pas par requête. Changer de modèle,
c'est redémarrer le processus — ce qui confirme la règle de l'étape 2 bis par une
autre voie.

**Deux réserves à lever en premier, avant d'écrire quoi que ce soit d'autre.**

1. **Pas de route vidéo.** Confirmé : `/v1/video/generations` rend 404. Il faut
   donc établir si `-M vid_gen` au démarrage du serveur fait produire des vidéos
   par `/v1/images/generations`, ou si la vidéo doit passer par la CLI. **Si c'est
   la CLI, les 111 secondes de chargement ne s'amortissent pas pour la vidéo** —
   ce qui change l'intérêt du serveur pour la moitié du travail. C'est la première
   chose à vérifier, elle prend dix minutes.

2. **Les paramètres ne sont pas ceux qu'on croit.** Un appel portant
   `"steps":4, "cfg_scale":1.0` a été exécuté avec `Steps: 20, CFG scale: 7.0` —
   les noms envoyés ont été ignorés en silence. Pour Flux schnell, 20 étapes à
   CFG 7 est un réglage faux. Les noms réellement acceptés sont à relever avant de
   bâtir dessus, sans quoi le moteur produira des images lentes et laides en
   croyant obéir.

---

## 4. Les deux pièges, mesurés

### a. Le binaire ne lit pas les chemins accentués — mais la solution est gratuite

`C:\Program Files\SenSÉ\...` échoue en `file not found` sur **tous** les arguments
de fichier. Le nom court 8.3 ne sauve pas : `SenSÉ` fait moins de huit caractères,
Windows ne lui génère pas d'alias. Passer par `CreateProcessW` avec des arguments
en UTF-16 ne sauve pas non plus — essayé, même échec. Le défaut est dans le
binaire, qui ouvre ses fichiers en octets étroits.

**Seuls les *arguments* sont touchés. Le répertoire courant, non.**

La solution tient en deux lignes et ne sort jamais du dossier du produit :

```csharp
psi.WorkingDirectory = <dossier des modèles>;   // peut porter l'accent
psi.ArgumentList.Add("--diffusion-model");
psi.ArgumentList.Add(@"unet\flux1-schnell-Q5_K_S.gguf");   // relatif, sans accent
```

Vérifié : Flux généré en 16,12 s par cette voie, depuis
`C:\Program Files\SenSÉ\Outils\...\models` comme répertoire courant.

**Corollaire de dessin :** tous les chemins passés à `sd-server` doivent être
relatifs à un dossier racine unique. Le manifeste de l'étape 2 doit donc décrire
les fichiers d'un modèle en chemins relatifs, jamais absolus.

Ce qu'il ne faut **pas** faire : créer une jonction ASCII hors du dossier produit.
C'est ce que les premiers essais ont utilisé, et c'est une extension du projet
au-delà de ses sources — inutile puisque le répertoire courant suffit.

### b. Wan 2.2 ne tient pas en VRAM par la voie directe

Chaque expert pèse **9,65 Go**, pour **10,79 Gio libres** sur les 12,28 de la
carte (Windows en occupe ~1,2). Le chargement direct échoue :

```
480×480×17 → manque 550 Mo
320×320×9  → manque 160 Mo
```

Réduire la définition ne suffit pas : ce sont **les poids** qui saturent, pas les
activations.

**La solution qui marche :** `--params-backend diffusion=cpu`. Les poids restent
en RAM, le calcul se fait sur la carte, les couches sont transférées à la volée.
C'est ainsi que les 63 secondes ci-dessus ont été obtenues.

`--offload-to-cpu` ne suffit pas — essayé, échoue.

**Conséquence de dessin :** le placement est une décision par exécution, pas une
constante. Flux tient en VRAM ; Wan n'y tient pas.

Bonne nouvelle : **`sd.cpp` sait déjà décider seul.** Il annonce son plan avant de
commencer, et le plan est juste pour Flux :

```
CUDA0  free 11071 MiB, budget 10559 MiB
RAM    free 20456 MiB, params budget 18408 MiB
DiT          params 7880 MiB -> compute CUDA0, params CUDA0
Conditioner  params 4776 MiB -> compute CUDA0, params cpu
VAE          params  319 MiB -> compute CUDA0, params cpu
auto-fit: --backend "diffusion=CUDA0,te=CUDA0,vae=CUDA0" --params-backend "te=cpu,vae=cpu"
```

Le moteur n'a donc pas à calculer le placement lui-même. Il doit :

1. laisser l'auto-fit décider par défaut ;
2. **forcer `--params-backend diffusion=cpu` pour les modèles vidéo**, où
   l'auto-fit se trompe et échoue après avoir chargé les poids — c'est-à-dire
   après dix secondes perdues ;
3. lire le plan annoncé et le journaliser, pour que l'échec soit lisible quand il
   arrive.

---

## 5. Le plan, par étapes

### Étape 1 — embarquer le binaire

Sur le modèle exact de `ServeurModele` (`src/SenSÉ.Tools/Assistant/`) :

- `Outils/sd.cpp/sd-server.exe` + `ggml-cuda.dll` + cudart.
- Un `ServeurDiffusion` calqué sur `ServeurModele` : démarrage paresseux,
  `ServeurLocal.Demarrer`, `Poids.Lourd`, `CoutVideoMo` réel, arrêt avec la
  fenêtre.
- Téléchargement au premier besoin, sur le modèle de `ChromiumEmbarque.cs`
  (`src/SenSÉ.Mcp.Bus/`) : révision épinglée, `.part` pendant la copie.

**Fini quand :**

- `GET /v1/models` répond depuis SenSÉ, modèle chargé.
- La fenêtre de l'Atelier se ferme → le processus meurt, vérifié au gestionnaire
  de tâches. Pas d'orphelin. (`JobEnfants` est le filet en dessous, comme pour les
  serveurs MCP.)
- Le binaire absent est dit clairement, sans planter l'Atelier.
- Les deux réserves du §3 sont levées et écrites ici même.

### Étape 2 — le rangement des modèles

```
Outils/Modeles/
  texte/      *.gguf          (llama.cpp)
  codage/     *.gguf          (llama.cpp)
  image/      unet, vae, text_encoders, loras   (sd.cpp)
  video/      unet, vae, text_encoders, loras   (sd.cpp)
  audio/      (à venir)
```

Les 66 Go actuels sont dans
`Outils/Comfy-Desktop/local/Comfy-Desktop/ComfyUI-Shared/models` :
unet 44 Go, checkpoints 9,0 Go, text_encoders 8,2 Go, loras 4,6 Go, vae 562 Mo.

**Tout est sur `C:` — le déplacement est un renommage, pas une copie. Il est
instantané.** Ne jamais copier ces 66 Go.

Un dossier ne dit pas quel moteur charger. **Un manifeste par modèle** est
nécessaire : moteur (`llama.cpp` / `sd.cpp`), coût VRAM, coût RAM, fichiers
compagnons (encodeur, VAE, LoRA). Sans lui, le chargeur devine — et il devinera
mal le jour où un format de plus arrive.

Ceci remplace la règle actuelle de `ServeurModele` — « un seul `.gguf` à la
racine, les autres dans `autres\` » — **qui est ce qui interdit aujourd'hui la
flottille de modèles**.

Forme suggérée du manifeste, à côté du poids :

```json
{
  "id": "wan22-t2v",
  "nom": "Wan 2.2 texte vers vidéo",
  "moteur": "sd.cpp",
  "espace": "Multimedia",
  "racine": "video",
  "fichiers": {
    "diffusion":            "unet/Wan2.2-T2V-A14B-LowNoise-Q4_K_M.gguf",
    "diffusion_haut_bruit": "unet/Wan2.2-T2V-A14B-HighNoise-Q4_K_M.gguf",
    "vae":                  "vae/wan_2.1_vae.safetensors",
    "t5xxl":                "text_encoders/umt5-xxl-encoder-Q4_K_M.gguf"
  },
  "vram_mo": 9651,
  "params_backend": "diffusion=cpu",
  "produit": "video"
}
```

Tous les chemins sont **relatifs à `racine`** — voir §4a, c'est ce qui contourne
l'accent. `vram_mo` sert à refuser une exécution avant de charger dix secondes de
poids pour rien.

**Fini quand :** les 66 Go sont sous `Outils/Modeles/{image,video}`, chaque modèle
a son manifeste, et un test lit les manifestes et vérifie que chaque fichier
déclaré existe. Ce test est le seul garde-fou contre un rangement à moitié fait.

### Étape 2 bis — un seul modèle à la fois, et l'ordre des nœuds

Les tailles mesurées ne laissent pas le choix :

| | poids en VRAM | carte |
|---|---|---|
| Flux schnell Q5_K_S | 7 880 Mio (DiT) | 12 282 Mio |
| Wan 2.2 A14B Q4, **un** expert | 9 651 Mio | dont ~11 000 utilisables |

**Deux modèles de diffusion ne tiennent jamais ensemble.** Ils travaillent chacun
leur tour, et le passage de l'un à l'autre est un rechargement — mesuré entre
**7 et 10 secondes** de lecture de tenseurs.

Trois conséquences pour le moteur :

1. `sd-server` garde **un** modèle chargé, pas une flottille. Charger le suivant
   décharge le précédent.
2. Le moteur doit **ordonner l'exécution du graphe par modèle** : tous les nœuds
   image, puis tous les nœuds vidéo. Un graphe qui alterne image/vidéo/image
   paierait deux rechargements pour rien — vingt secondes gaspillées sur un
   graphe de trois nœuds.
3. Un nœud doit pouvoir dire de quel modèle il dépend **avant** de s'exécuter,
   sinon l'ordonnancement du point 2 est impossible. C'est une propriété de la
   `DefinitionNoeud`, à ajouter.

C'est aussi la raison pour laquelle le texte reste sur processeur et RAM : la
carte est déjà pleine avec un seul modèle de diffusion.

### Étape 3 — les nœuds Multimedia

Réécrire `Bibliotheque/Multimedia/Tous.cs` contre `ServeurDiffusion` au lieu de
`ClientComfyui`. Supprimer `Mcp/ClientComfyui.cs`.

Nœuds minimaux :

- `texte_vers_image` — Flux. Existe déjà, change de moteur.
- `texte_vers_video` — Wan T2V. Nouveau.
- `image_vers_video` — Wan I2V. Aujourd'hui un `Fail(...)` ; devient réel.
- `charger_modele` / `lora` — pour que le graphe dise quel poids il veut, au lieu
  de le supposer.

**Fini quand :** un graphe à trois nœuds — texte → image → vidéo — s'exécute de
bout en bout sans que ComfyUI tourne. C'est le seul critère qui compte, et il est
binaire.

Deux gardes à ne pas oublier :

- Une exécution refusée **avant** le chargement quand la VRAM manque, avec le
  chiffre manquant dans le message. Aujourd'hui l'échec arrive après dix secondes
  de chargement et dit `cannot make enough memory available on CUDA0`, ce qui ne
  se lit pas.
- La sortie est écrite en `.avi` MJPEG même quand on demande `.mp4` (§annexe). Le
  nœud doit convertir avec le `ffmpeg` déjà embarqué, ou nommer le fichier pour ce
  qu'il est. Rendre un `.mp4` qui n'en est pas un ferait échouer l'outil suivant.

### Étape 4 — le raccordement au shell

Chaque nœud exécutable devient une commande SenSÉ. C'est ici que le chantier paie :
le shell peut commander la génération parce qu'elle est devenue **un processus
SenSÉ avec des verbes SenSÉ**, et non un appel HTTP vers un étranger.

**La règle du produit tient par construction** : le système ne peut émettre que
des commandes que l'utilisateur aurait pu taper. Ce n'est pas une contrainte à
contourner, c'est la définition du vocabulaire.

Les verbes MCP de l'Atelier existent déjà (`executer`, `executer_noeud`,
`annuler`, `refaire`, paramètre `espace`). Il manque de quoi lister ce qui est
disponible et suivre ce qui est long :

- `modeles` — ce qui est installé, par espace, avec le coût VRAM.
- `etat` — ce qu'une exécution fait en ce moment, puisqu'une vidéo prend trois
  minutes et qu'un appel qui ne rend rien pendant trois minutes se lit comme une
  panne.

**Fini quand :** une commande du shell produit une image, sans passer par la
fenêtre de l'Atelier, et l'utilisateur peut taper la même commande lui-même.

### Étape 5 — Comfy dégage

Une fois l'étape 3 verte :

```
Outils/Comfy-Desktop/       ~8 Go   (moins les 66 Go de modèles, déjà déplacés)
Outils/ComfyUI/                     (dossiers vides de liaison)
Outils/Python/               5,0 Go (à vérifier : qui d'autre en dépend ?)
Plugins/comfyui/
Plugins/dependance-comfyui-desktop/
Plugins/dependance-python/
```

Côté code, 26 fichiers mentionnent Comfy. Le gros est
`src/SenSÉ.Tools/Generation/` — `ComfyServer`, `Catalogue`, `FluxTravail`,
`FluxGel`, `WorkflowsLivres`, `SequenceAnimee`, `PaquetGenerateur`,
`OptionsVivantes`. **Ne rien supprimer là avant que l'étape 3 ne soit verte** :
c'est aujourd'hui la seule voie de génération d'image du produit.

---

## 5 bis. L'ordre, et ce qu'il ne faut pas faire

### L'ordre

Les étapes 1 et 2 sont indépendantes et peuvent se mener en parallèle. Tout le
reste s'enchaîne :

```
1 (serveur)  ─┐
              ├─→ 3 (nœuds) ─→ 4 (shell) ─→ 5 (Comfy dégage)
2 (modèles)  ─┘
      └─ 2 bis (un seul à la fois) est une règle, pas une étape :
         elle contraint 1 et 3, elle ne se développe pas seule.
```

**Rien ne se supprime avant que l'étape 3 ne soit verte.** C'est la seule règle
d'ordonnancement qui compte.

### Ce qu'il ne faut pas faire

- **Ne pas copier les 66 Go.** Tout est sur `C:` ; le déplacement est un
  renommage, instantané. Une copie prendrait une heure et doublerait l'occupation
  du disque le temps de la faire.
- **Ne pas supprimer `src/SenSÉ.Tools/Generation/` avant l'étape 3.** C'est
  aujourd'hui la seule voie de génération d'image du produit, et vingt-six
  fichiers en dépendent.
- **Ne pas créer de dossier hors de `C:\Program Files\SenSÉ`.** Ni jonction, ni
  dossier de travail, ni sortie. Le §4a règle le problème de l'accent sans sortir
  du produit.
- **Ne pas croire le succès sur parole.** Le binaire annonce `save result` et
  écrit un `.avi` quand on demande un `.mp4` ; il accepte `steps:4` et exécute
  `Steps: 20`. Chaque étape doit vérifier son résultat sur le disque, pas dans le
  message.
- **Ne pas chercher à tenir deux modèles de diffusion en VRAM.** Mesuré, ils n'y
  tiennent pas. Un moteur qui l'espère échouera après dix secondes de chargement,
  au hasard des autres applications ouvertes.

### Ce qui se teste sans carte graphique

Utile, parce que la suite de tests du produit tourne sans GPU :

- La lecture des manifestes et l'existence des fichiers déclarés.
- L'ordonnancement d'un graphe par modèle — un graphe qui alterne image et vidéo
  doit produire deux chargements, pas quatre. Cela se vérifie sur un faux moteur
  qui compte les chargements, sans rien générer.
- La construction de la ligne de commande : chemins relatifs, jamais absolus.
- Le refus avant chargement quand la VRAM déclarée manque.

Ce qui exige la carte — la génération elle-même — reste vérifié à la main, avec
les chiffres du §3 comme référence.

---

## 6. Ce qui reste à décider

**La qualité contre le temps — mesuré depuis.** La longueur native de Wan 2.2,
81 images à 16 i/s :

```
480×480, 81 images (5,06 s de vidéo), 2+2 étapes
→ 178,26 s au total, dont 67,31 s d'échantillonnage
```

**Le chargement coûte plus cher que le calcul : 111 s contre 67.** Encodeur,
expert haut bruit, expert bas bruit, VAE — quatre chargements, à chaque
invocation de la CLI.

C'est l'argument décisif pour l'étape 1. **`sd-server` garde ses poids chargés :
une seconde vidéo avec le même modèle coûterait ~67 s au lieu de 178.** Embarquer
le serveur plutôt que d'appeler la CLI ne fait pas gagner quelques pour cent, il
divise le temps par deux et demi.

Ce qui reste non mesuré : 832×480 (la définition native de Wan 480p) et un nombre
d'étapes réaliste sans les LoRA 4 étapes.

**Le partage de la VRAM.** Décidé : le média prend la carte, le texte reste sur
processeur et RAM. Le moteur doit donc savoir refuser une génération quand la
carte est déjà prise, plutôt que d'échouer à mi-parcours — le message
`cannot make enough memory available on CUDA0` arrive après le chargement des
poids, c'est-à-dire après dix secondes perdues.

**Les autres formats.** `svd_xt.safetensors` (Stable Video Diffusion) est dans
les checkpoints et n'a pas été essayé.

---

## Annexe — la ligne de commande qui a marché

```
sd-cli.exe -M vid_gen \
  --diffusion-model            <...>/Wan2.2-T2V-A14B-LowNoise-Q4_K_M.gguf \
  --high-noise-diffusion-model <...>/Wan2.2-T2V-A14B-HighNoise-Q4_K_M.gguf \
  --vae                        <...>/wan_2.1_vae.safetensors \
  --t5xxl                      <...>/umt5-xxl-encoder-Q4_K_M.gguf \
  --lora-model-dir             <...>/loras \
  -p "... <lora:wan2.2_t2v_lightx2v_4steps_lora_v1.1_high_noise:1>" \
  --video-frames 17 -H 480 -W 480 \
  --steps 2 --high-noise-steps 2 \
  --cfg-scale 1.0 --high-noise-cfg-scale 1.0 \
  --params-backend diffusion=cpu --diffusion-fa --vae-tiling \
  -o sortie.mp4
```

Les chemins doivent être **sans accent** (voir §4a). La sortie est écrite en
`.avi` MJPEG même si l'extension demandée est `.mp4` — à convertir avec le
`ffmpeg` déjà embarqué, ou à accepter tel quel.
