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

**Il fournit aussi `sd-server.exe`** — la même forme que `llama-server.exe`. C'est
lui qu'il faut embarquer, pas la CLI : un processus qui vit, qui garde ses poids
chargés, et qu'on arrête avec la fenêtre.

---

## 4. Les deux pièges, mesurés

### a. Le binaire ne lit pas les chemins accentués

`C:\Program Files\SenSÉ\...` échoue en `file not found`, sur **tous** les
arguments de fichier. Le nom court 8.3 ne sauve pas : `SenSÉ` fait moins de huit
caractères, Windows ne lui génère pas d'alias.

Trois issues, à trancher :

1. **Une jonction ASCII** créée au démarrage (`C:\sense-modeles` → le vrai
   dossier). Instantané, pas de copie. C'est ce qui a servi aux essais.
2. **Ranger les modèles hors du dossier produit**, sous un chemin sans accent.
3. Corriger `stable-diffusion.cpp` en amont — le plus propre, le plus lent.

L'option 1 est recommandée pour démarrer : elle ne déplace pas un octet et se
défait d'une ligne.

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

**Conséquence de dessin :** le moteur doit choisir son placement selon le modèle
et la VRAM libre au moment de l'appel, pas selon une constante écrite en dur.
Flux tient en VRAM ; Wan n'y tient pas. Le placement est une décision par
exécution.

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

**Ce qui est fini quand :** `sd-server` répond sur son port, et s'arrête quand la
fenêtre se ferme. Rien d'autre.

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

### Étape 3 — les nœuds Multimedia

Réécrire `Bibliotheque/Multimedia/Tous.cs` contre `ServeurDiffusion` au lieu de
`ClientComfyui`. Supprimer `Mcp/ClientComfyui.cs`.

Nœuds minimaux :

- `texte_vers_image` — Flux. Existe déjà, change de moteur.
- `texte_vers_video` — Wan T2V. Nouveau.
- `image_vers_video` — Wan I2V. Aujourd'hui un `Fail(...)` ; devient réel.
- `charger_modele` / `lora` — pour que le graphe dise quel poids il veut, au lieu
  de le supposer.

### Étape 4 — le raccordement au shell

Chaque nœud exécutable devient une commande SenSÉ. C'est ici que le chantier paie :
le shell peut commander la génération parce qu'elle est devenue **un processus
SenSÉ avec des verbes SenSÉ**, et non un appel HTTP vers un étranger.

**La règle du produit tient par construction** : le système ne peut émettre que
des commandes que l'utilisateur aurait pu taper. Ce n'est pas une contrainte à
contourner, c'est la définition du vocabulaire.

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

## 6. Ce qui reste à décider

**La qualité contre le temps.** Les 63 secondes ci-dessus valent pour 480×480,
17 images, 2+2 étapes. C'est un essai de faisabilité, pas un réglage. Ce que
coûte une vidéo utilisable — 832×480, 81 images — n'a pas été mesuré et se
compte probablement en dizaines de minutes.

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
