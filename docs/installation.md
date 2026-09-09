# S'installer, se connecter, s'approvisionner

*10 septembre 2026. Suite de [arborescence.md](arborescence.md). Chiffres mesurés sur la machine de développement.*

---

## 1. L'écart, mesuré

| | poids |
|---|---:|
| Ce que l'installeur livre aujourd'hui | **1,86 Go** — 15 binaires, rien d'autre |
| Ce qu'il faut pour que le produit fasse ce qu'il promet | **~80 Go** |

Une installation fraîche donne donc un produit qui démarre et ne sait rien faire : pas de modèle, pas de navigateur, pas de ffmpeg, pas de moteur de langage. Tout est à télécharger, et **rien ne le télécharge**.

Détail de ce qui manque :

| | poids |
|---|---:|
| `Moteurs/video/` — Wan 2.2, SVD, LoRA | **53,07 Go** |
| `Moteurs/image/` — Flux schnell, T5, CLIP, VAE | **12,80 Go** |
| `Moteurs/texte/` — Qwen 3.5 9B + projecteur | **6,15 Go** |
| `Moteurs/codage/` — Qwen 2.5 Coder 3B | 1,80 Go |
| `Moteurs/audio/` — Whisper large-v3-turbo | 0,53 Go |
| `Programmes/Langages/` | 2,04 Go |
| `Programmes/Chromium/` | 0,81 Go |
| `Programmes/ffmpeg/` | 0,42 Go |
| `Programmes/Python/`, `SearXNG/`, `llama.cpp/`, `Real-ESRGAN/`, `rife/` | 0,55 Go |

---

## 2. Le principe : des paliers, jamais tout ou rien

**Personne n'a besoin de 80 Go, et le croire est le meilleur moyen de faire abandonner l'installation.**

La vidéo pèse à elle seule les deux tiers du total. Un assistant qui parle, code, écoute et cherche sur le web tient dans **neuf gigaoctets** — et c'est ce que l'immense majorité des gens veut.

| palier | contient | poids | ce que ça rend possible |
|---|---|---:|---|
| **Socle** | le produit seul | 1,9 Go | manettes, clavier virtuel, outils, Atelier — **sans IA** |
| **Assistant** | Qwen 9B + projecteur, Coder 3B | +7,95 Go | dialogue, vision, code, aiguillage |
| **Voix** | Whisper turbo | +0,53 Go | parole vers texte |
| **Web** | Chromium, SearXNG | +0,92 Go | recherche traçable, pilotage de webapps |
| **Médias** | ffmpeg, Real-ESRGAN, rife | +0,49 Go | conversion, agrandissement, interpolation |
| **Langages** | 14 runtimes | +2,04 Go | l'Atelier exécute ce qu'il écrit |
| **Image** | Flux schnell | +12,80 Go | génération d'images |
| **Vidéo** | Wan 2.2, SVD | +53,07 Go | génération de vidéos |

**Socle + Assistant + Voix + Web = 11,3 Go.** C'est l'installation par défaut à proposer. Le reste s'ajoute quand on en a besoin, et se retire quand on n'en a plus.

Corollaire : **l'installeur ne bloque pas sur les gros paliers.** Dès que le palier Assistant est descendu, le produit est utilisable ; l'image et la vidéo continuent en tâche de fond, ou attendent la nuit.

---

## 3. Se connecter — ce que ça veut dire vraiment

Pour un système destiné à devenir autonome, « avoir internet » n'est pas un booléen.

### Vérifier une vraie connexion, pas une carte réseau

Une carte active ne veut rien dire. Ce qui compte est qu'un point précis réponde. À vérifier dans cet ordre :

1. Résolution DNS.
2. Un HTTPS vers l'hôte **dont on va réellement dépendre** — pas vers un site témoin. Si le miroir des modèles est injoignable, savoir que Google répond n'aide personne.

### Le portail captif

Hôtel, aéroport, wifi d'entreprise : la requête part, quelque chose répond, et ce n'est pas ce qu'on a demandé. La signature est un **code inattendu ou une redirection vers un autre hôte**.

Ce n'est pas théorique : le 9 septembre, la façade de recherche a répondu **HTTP 202 avec un contrôle anti-robot** au lieu de résultats. Le code corrige déjà ce cas — il le nomme au lieu de rendre « aucun résultat ». **La même règle vaut ici** : un téléchargement qui ramène du HTML au lieu d'un binaire doit dire *« un portail intercepte la connexion »*, jamais *« le fichier est corrompu »*.

### Le proxy

Honorer le proxy système et les fichiers PAC. Sur une machine d'entreprise c'est la différence entre « ça marche » et « ça ne marchera jamais, et personne ne saura pourquoi ».

### Rester installable sans internet

**Un système qui ne s'installe pas sans réseau n'est pas autonome, il est dépendant.**

Il faut donc, dès le départ, une **archive compagnon** — clé USB, disque externe — que l'installeur accepte à la place du téléchargement. Le catalogue est le même ; seule la source change. C'est aussi ce qui rend une salle de classe, un atelier sans wifi ou une machine hors ligne installables.

### Survivre à la coupure

53 Go ne descendent pas d'un trait. Il faut :

- la **reprise par plages HTTP** (`Range`) — reprendre où on s'est arrêté, pas au début ;
- la **survie au redémarrage** — l'état de l'approvisionnement sur le disque, pas en mémoire ;
- l'**écriture en `.part`**, renommée seulement une fois le fichier complet et vérifié.

Les deux derniers points existent déjà et fonctionnent : `ChromiumEmbarque` télécharge 806 Mo de cette façon depuis des mois. **Il n'y a pas à les inventer, seulement à les généraliser.**

---

## 4. L'intégrité — ce qui manque, et qui compte le plus

**Télécharger 80 Go sans vérifier ce qu'on reçoit, c'est ouvrir la porte la plus large du produit.**

Ce n'est pas une inquiétude abstraite. Hier, en installant SearXNG : `pip install searxng` **n'installe pas SearXNG**. Le paquet de ce nom sur PyPI est publié par un tiers, en version 0.1.2, sans rapport avec le projet officiel. Le geste évident était le mauvais.

Un catalogue qui aurait dit « URL, version, empreinte » aurait rendu ce piège impossible.

### Les quatre règles

1. **Rien ne se télécharge sans être déclaré** — URL exacte, version épinglée, taille, SHA-256.
2. **L'empreinte est vérifiée avant usage**, jamais après. Un fichier qui ne correspond pas est effacé, pas mis de côté.
3. **La version est épinglée, jamais « la dernière ».** `ChromiumEmbarque` l'a déjà appris et l'a écrit : *« sans quoi "télécharger une fois" deviendrait "télécharger la version du jour", et le comportement changerait sous les pieds de l'utilisateur »*.
4. **Quand une source ne publie pas d'empreinte, le catalogue le dit.** Les instantanés Chromium n'en publient pas ; le manifeste porte alors `empreinte_publiee: false`, et l'installeur **le montre** au lieu de sauter la vérification en silence. Une vérification absente et sue vaut mieux qu'une vérification absente et cachée.

---

## 5. Le catalogue

Le mécanisme existe **à moitié**. Les plugins `dependance-*` déclarent déjà `id`, `name`, `author`, `licence`, `hint`, `target`, `source` — c'est la bonne forme. Mais :

- `source` est parfois une **page de téléchargement** (`libreoffice.org/download/…`), que seule une personne peut suivre ;
- il n'y a **ni empreinte, ni taille, ni version épinglée**.

Un manifeste utilisable par une machine :

```json
{
  "id": "moteur-texte-qwen35-9b",
  "palier": "assistant",
  "nom": "Qwen 3.5 9B Q4_K_M",
  "url": "https://…/Qwen3.5-9B-Q4_K_M.gguf",
  "version": "Q4_K_M-2026-07",
  "octets": 5680522464,
  "sha256": "…",
  "empreinte_publiee": true,
  "vers": "Moteurs/texte/Qwen3.5-9B-Q4_K_M.gguf",
  "licence": "Apache-2.0",
  "requis_par": ["dialogue", "vision"]
}
```

Un seul fichier, versionné avec le produit, décrivant **tout** ce qui se télécharge — modèles, programmes, runtimes. C'est lui que l'archive hors ligne accompagne, et lui que l'installeur lit.

---

## 6. L'approvisionneur

Une classe `Approvisionnement`, qui généralise ce que `ChromiumEmbarque` fait déjà bien :

| déjà éprouvé | à ajouter |
|---|---|
| écriture `.part`, renommage atomique | reprise par plages HTTP |
| version épinglée dans un fichier témoin | vérification SHA-256 |
| vérification que l'archive s'ouvre | état persistant sur le disque |
| HTTPS vers la source officielle | parallélisme borné (2 ou 3, pas 10) |
| | provenance écrite, comme `Reçu/` |

**La provenance relie ce document au précédent** : chaque chose téléchargée laisse d'où elle vient, quand, et avec quelle empreinte. Six mois plus tard, « d'où sort ce fichier de 9 Go » a une réponse.

---

## 7. Le premier démarrage

1. **Vérifier la connexion** — et si elle manque, proposer l'archive compagnon plutôt que de s'arrêter.
2. **Montrer les paliers**, cochés par défaut sur *Socle + Assistant + Voix + Web = 11,3 Go*, avec le poids et une estimation de durée mesurée sur les premiers mégaoctets.
3. **Descendre le palier Assistant d'abord**, et rendre la main dès qu'il est là. Le reste continue derrière.
4. **Écrire ce qui est arrivé** — un état lisible, repris tel quel au prochain démarrage si la machine s'éteint.

---

## 8. Ce qui bloque aujourd'hui

**L'updater vise encore SteamXBox.** Mesuré dans `updater/updates.json` :

```
"productName": "SteamXBox"
"key": "SOFTWARE\\Hoyatla\\SteamXBox"
"helpUrl": "https://github.com/Hoyatla/SteamXBox-Explorer"
```

Le renommage v0.6.0 ne l'a pas suivi. Une installation neuve enregistrerait et vérifierait **le mauvais produit** — à corriger avant tout travail sur l'installeur, sinon on bâtira dessus.

**Deux `source` ne sont pas des fichiers** — LibreOffice et Poppler pointent vers des pages. Elles resteront manuelles tant qu'on n'aura pas d'URL directe, et le catalogue doit pouvoir dire « celle-ci, c'est à toi de la faire » sans faire échouer le reste.

**Poppler est en GPL** et ne peut pas être livré. Le manifeste porte déjà cette information ; l'installeur doit la respecter — c'est-à-dire proposer, pas installer.

---

## 9. Ordre proposé

1. **Corriger l'updater** — sinon tout ce qui suit s'appuie sur un mauvais identifiant.
2. **Le catalogue** : un manifeste, avec empreintes et versions épinglées, pour ce qui est déjà installé ici. Il se remplit en mesurant la machine actuelle.
3. **`Approvisionnement`** : généraliser `ChromiumEmbarque`, ajouter empreinte, reprise et état persistant.
4. **Les paliers** dans l'installeur, avec le défaut à 11,3 Go.
5. **La connexion** : vérification réelle, proxy, portail captif nommé.
6. **L'archive compagnon** pour l'installation hors ligne.
7. **La provenance** de chaque téléchargement, au format de `Reçu/`.

---

*Rien ici ne sort de `C:\Program Files\SenSÉ`.*
