# L'orchestrage multimodal de SenSÉ

**Un orchestrateur réparti, une flottille de spécialistes, et une règle matérielle
qui décide de tout le reste.**

Toutes les mesures de ce document ont été relevées les 7 et 8 septembre 2026 sur
la machine de référence — i7-14700KF (20 cœurs / 28 fils), 31,8 Gio de RAM,
RTX 4070 SUPER 12 Gio. Aucune n'est estimée.

---

## 1. Le principe : deux voies matérielles

C'est le fait le plus important du système, et il a été mesuré plutôt que
supposé.

**Les modèles de langage tournent sur le processeur** (`-ngl 0`, invariante « jamais
la carte »). **Les modèles d'image, de vidéo et d'audio tournent sur la carte.**

Conséquence, mesurée le 8 septembre : Whisper a transcrit **vingt-cinq fois**
pendant que le Qwen 9B rédigeait deux cents jetons.

| État du 9B | Génération |
|---|---|
| seul | 8,25 j/s |
| Whisper résident, inactif | 7,87 j/s |
| **Whisper transcrivant en même temps** | **7,87 j/s** |

**Identique à la décimale.** Le parallélisme entre les deux voies est gratuit
parce qu'elles n'utilisent pas le même matériel.

Ce n'est pas un arrangement de circonstance : **c'est ce qui rend le système
multimodal possible.** Le partage VRAM / RAM décidé au départ produit exactement
cette propriété.

---

## 2. La table des moteurs

| Rôle | Modèle | Moteur | Voie | Port | Poids |
|---|---|---|---|---|---|
| **orchestre** | Qwen 2.5 Coder 3B | llama.cpp | processeur | 8082 | (mêmes poids que codage) |
| **texte** | Qwen 3.5 9B + projecteur | llama.cpp | processeur | 8081 | 6,15 Go |
| **codage** | Qwen 2.5 Coder 3B | llama.cpp | processeur | 8083 | 1,80 Go |
| **audio** | Whisper large-v3-turbo | whisper.cpp | **carte** | 8084 | 0,57 Go |
| **image** | Flux schnell | sd.cpp | **carte** | 8085 | 7,88 Go (VRAM) |
| **vidéo** | Wan 2.2 T2V / I2V | sd.cpp | **carte** | 8085 | 9,65 Go par expert |

Les trois moteurs sont de la même famille — `llama.cpp`, `whisper.cpp`,
`sd.cpp` — donc le même patron : un binaire natif autonome, format ggml/gguf,
pas de Python, un serveur HTTP sur la boucle locale, arrêté avec la fenêtre.

**Un dossier par rôle** dans `Outils/Modeles/`, chacun avec son `modele.json` :
`orchestre`, `texte`, `codage`, `audio`, `image`, `video`, `vision`. Remplacer un
modèle, c'est déposer le fichier et corriger son manifeste — rien d'autre.

`vision/` est vide et volontairement : la lecture d'images passe aujourd'hui par
le projecteur du modèle de texte. Le dossier attend un modèle visuel dédié, le
jour où il vaudra sa place.

---

## 3. Résidence et tour de rôle

Trois règles, chacune adossée à une mesure.

### a. Rester chargé coûte de la mémoire, et presque rien d'autre

Whisper résident mais inactif a coûté 5 % au 9B. Un modèle chargé qui ne calcule
pas est essentiellement gratuit.

**Corollaire :** ne jamais décharger un modèle qui resservira. Un rechargement
coûte **7 à 10 secondes** de lecture de tenseurs, mesuré sur les modèles de
diffusion.

### b. Deux moteurs sur la même voie se partagent le calcul

Le parallélisme n'est gratuit qu'**entre** les voies. L'aiguilleur et le 9B sont tous
deux sur le processeur : résidents ensemble, oui ; calculant ensemble, ils se
partagent les cœurs.

Mesuré autrement, le 7 septembre : un témoin monofil chronométré pendant qu'un
seul `llama-server` travaillait passait de 2 107 ms à 3 171 ms — **la moitié de
la machine part dans le modèle**. Deux modèles ne feraient pas mieux que se la
diviser.

**Règle : résidence partagée, calcul à tour de rôle sur une même voie.**

### c. Le budget mémoire ferme la question

```
orchestre + codage  Qwen Coder 3B      1,80 Go   (un fichier, deux ports)
texte               Qwen 9B + mmproj   6,15 Go   (7,03 Gio résidents avec son cache)
audio               Whisper turbo      0,57 Go
                                       ────────
poids                                  8,52 Go   sur 31,8 Gio
```

Les trois tiennent en RAM. Ce qui ne tient pas, c'est leur calcul simultané — et
la règle (b) l'interdit déjà.

Côté carte, la contrainte est plus dure : **un seul modèle de diffusion à la
fois** (9,65 Go par expert Wan sur 12,28 Gio), et Whisper doit lui laisser la
place. Whisper occupe 3,3 Go pendant qu'il travaille ; il faut donc l'arrêter ou
attendre avant de lancer une vidéo.

---

## 4. Qui choisit quoi

### Les règles avant le modèle

Beaucoup de dispatches se décident sans rien demander à personne : un `.wav`
déposé va à l'audio, un `.png` à la vision, « écris-moi un script » au codage.
**Ce qui se décide gratuitement ne doit pas coûter une génération.**

### Le modèle décide le reste

L'orchestrateur reçoit la liste des voies disponibles avec leur coût, et une
règle courte :

> Choisis la voie la moins chère qui sache faire la tâche. À capacité égale, la
> plus rapide. Ne demande pas une voie absente de la liste.

**Son invite doit rester minuscule.** C'est le point que la mesure a renversé :
l'aiguillage ne coûte pas cher à cause du nombre de paramètres, mais à cause de
la longueur de l'invite. Une invite de 200 jetons et trois jetons de réponse
coûtent une demi-seconde ; la même décision prise derrière les 6 500 jetons de
consigne du modèle de dialogue en coûterait douze.

**C'est pourquoi l'aiguilleur a son propre serveur et son propre port.** Pas parce qu'il
doit être petit — parce qu'il ne doit pas partager le cache de préfixe du modèle
de dialogue. Réécrire le message système invalide ce cache en entier, et le tour
suivant repaie l'ingestion complète.

### Ce que l'orchestrateur ne décide pas

Le téléchargement d'un modèle, l'installation d'un moteur de langage, tout acte
irréversible. Le modèle propose, l'environnement dessine la question, l'utilisateur
tranche. C'est la règle du produit et elle ne s'assouplit pas ici.

---

## 5. Comment ils s'échangent l'information

**Il n'y a pas de protocole à inventer.** Les trois moteurs parlent déjà HTTP sur
`127.0.0.1` :

| Moteur | Interface |
|---|---|
| llama.cpp | `POST /v1/chat/completions` — dialecte OpenAI, appel d'outils par `--jinja` |
| sd.cpp | `POST /v1/images/generations` — dialecte OpenAI, rend du base64 |
| whisper.cpp | `POST /inference` — fichier audio en `multipart`, rend le texte |

Ce qui circule entre eux est donc du **texte et des chemins de fichiers**, pas des
objets. Whisper rend une transcription que l'orchestrateur passe au modèle de
dialogue ; le modèle de codage rend un script que l'Atelier exécute ; sd.cpp rend
une image dont le chemin devient l'entrée du nœud suivant.

C'est déjà le mécanisme de `FichierProduit` côté Assistant : un outil nomme le
fichier qu'il vient d'écrire, le suivant le reprend tel quel. **L'orchestrage est
une chaîne de chemins, pas un bus de messages.**

---

## 6. Ce qui doit changer dans le code

Quatre lignes interdisent aujourd'hui la flottille entière :

```csharp
ServeurModele.cs:121   Directory.GetFiles(DossierModeles, "*.gguf")   // premier niveau seulement
ServeurModele.cs:47    private const int Port = 8081;                 // un port
ServeurModele.cs:370   "--parallel", "1"                              // un emplacement
                       ServeurLocal.Demarrer  ×1                      // un processus
```

**Un `.gguf` à la racine, un port, un processus.** C'est pourquoi essayer le 9B a
demandé de *remplacer* le 4B au lieu d'ajouter.

`ServeurModele` doit devenir une **table de moteurs** : lire les `modele.json`,
démarrer un serveur par rôle demandé sur son port, garder ce qui est chargé, et
n'arrêter que ce que la fenêtre ferme. Les manifestes existent déjà et portent ce
qu'il faut — moteur, port, contexte, arguments, mesures.

C'est le même chantier que celui décrit dans `src/SenSÉ.Atelier/PLAN-MOTEUR.md`
étape 2, où le rangement des modèles de diffusion a déjà été fait.

---

## 7. Les étapes

**1 — La table.** Lire les manifestes, démarrer un moteur par rôle, un port
chacun. *Fini quand :* deux modèles de langage tournent en même temps sur deux
ports, et que fermer la fenêtre les arrête tous les deux sans orphelin.

**2 — L'audio.** `whisper-server` embarqué comme `llama-server` l'est. C'est le
plus simple des trois moteurs et il ne dispute rien au processeur. *Fini quand :*
un fichier son déposé dans l'Assistant devient du texte.

**3 — L'aiguillage.** Le Coder 3B sur un second port, son invite courte, les règles avant le
modèle, et le choix inscrit après coup. *Fini quand :* une même demande aiguille
deux fois vers la même voie, et qu'une voie absente est dite plutôt que tentée.

**4 — Le codage.** Qwen Coder branché sur l'espace Codage de l'Atelier — et là,
le plan des langages prend le relais : un modèle qui écrit du Rust ne sert à rien
tant que rien ne le compile. Voir `src/SenSÉ.Atelier/PLAN-LANGAGES.md`.

---

## 8. Ce qui n'est pas tranché

**La cohabitation carte.** Whisper prend 3,3 Go de VRAM en travaillant, Wan en
demande 9,65 par expert sur 12,28. Ils ne tiennent pas ensemble. Faut-il arrêter
Whisper avant une vidéo, attendre, ou refuser ? La règle n'existe pas encore.

**Le premier chargement.** Quatre moteurs à démarrer, c'est quatre attentes. Tout
charger à l'ouverture rendrait le démarrage interminable ; tout charger à la
demande fait payer 7 à 10 secondes au premier usage de chaque voie. Il faut
choisir, probablement par rôle.

**Ling a été mesuré, et écarté.** Voir §9.

---

## 9. L'aiguilleur, mesuré — et le résultat n'est pas celui qu'on attendait

Banc du 9 septembre 2026 : huit demandes à router vers cinq voies, même invite
système, mêmes trois exemples, même température nulle, tous sur processeur seul.

| Candidat | Justes | Latence moyenne | Poids |
|---|---|---|---|
| Ling 3.0 tiny, réflexion coupée | 4/8 | 284 ms | 4,58 Go |
| Ling 3.0 tiny, réflexion rendue | 6/8 | **10 081 ms** | 4,58 Go |
| Qwen 3.5 9B | 7/8 | 848 ms | 6,15 Go |
| **Qwen 2.5 Coder 3B** | **7/8** | **232 ms** | **1,80 Go** |

**Ling est écarté du rôle.** Il raisonne par défaut, et sa réflexion coûte dix
secondes par décision — rédhibitoire pour un aiguilleur dont tout l'intérêt est
d'être bon marché. Réflexion coupée, il répond en 284 ms mais ne juge juste
qu'une fois sur deux : il répond à la demande au lieu de l'aiguiller, ou colle à
sa réponse précédente.

**Ce que ce banc n'établit pas :** huit cas, une invite, une tâche. Il ne dit rien
de Ling sur du travail lourd en raisonnement, où il est peut-être excellent. Il
dit seulement qu'il n'est pas l'aiguilleur de ce système.

### Le remplaçant était déjà là

**Qwen 2.5 Coder 3B tient l'aiguillage : 7/8 en 232 ms.** La meilleure justesse
*et* la meilleure latence des quatre, pour le plus petit des modèles — et il est
déjà dans la flottille pour le codage.

Son unique erreur est prévisible et corrigible : « Explique-moi la différence
entre RAM et VRAM » est parti vers `codage`. C'est un modèle de code, une question
technique lui ressemble à du code. Une ligne d'invite suffit — dire qu'une
question à laquelle *on répond en prose* va vers `texte`, même technique.

### Ce que cela change à la table du §2

Un modèle de moins, et le plus gros des petits. La flottille passe de 13,10 Go de
poids à **8,52 Go** :

```
codage + orchestre   Qwen 2.5 Coder 3B   1,80 Go   (deux rôles, un fichier)
texte                Qwen 3.5 9B          6,15 Go
audio                Whisper turbo        0,57 Go
                                          ────────
                                          8,52 Go
```

**Réserve à tenir :** aiguiller et coder demandent deux invites système
différentes. Alterner deux invites système sur **un** serveur invalide le cache de
préfixe à chaque bascule — le même piège qu'au §4. Il faut donc deux serveurs sur
deux ports, lisant le même fichier de poids.

### Et Ling ?

Il reste sur le disque, son manifeste marqué `"actif": false` avec le détail de la
mesure. Un modèle qui raisonne dix secondes n'est pas mauvais : il est mal
employé. S'il trouve un rôle où l'on paie volontiers dix secondes pour une
meilleure réponse, il est là.

---

## 10. Le généraliste comme second avis

Le 9B n'est pas seulement le modèle de dialogue. C'est un généraliste : il écrit
du code, il lit des images, il raisonne sur ce que les spécialistes produisent.
Et le banc du §9 a montré qu'il aiguille aussi bien que l'aiguilleur, en 848 ms
au lieu de 232.

**Il y a donc un arbitre déjà résident, et cela ne coûte rien de plus.**

Trois emplois qui découlent de la mesure, et non d'une intuition :

**Départager un aiguillage douteux.** L'aiguilleur se trompe une fois sur huit, et
son erreur est d'un type connu — une question technique lui paraît du code. Quand
sa réponse est hors liste, ou qu'il hésite entre deux voies, le 9B tranche pour
600 ms de plus. On paie l'arbitrage seulement quand il sert.

**Relire ce que le spécialiste a produit.** Un modèle de 3 milliards de paramètres
écrit du code plausible ; un de 9 milliards voit mieux ce qui cloche. La relecture
n'a de sens que dans ce sens-là — le petit ne corrigera pas utilement le gros.

**Décrire une image que le système vient de fabriquer.** Flux produit, le 9B
regarde. C'est la seule voie qui permette au système de vérifier son propre
travail visuel sans demander à l'utilisateur.

### Ce que le 9B fait et ne fait pas — vérifié

Son projecteur déclare `clip.has_vision_encoder` et une vingtaine de clés
`clip.vision.*`. **Aucune clé audio.**

| | 9B |
|---|---|
| discuter, raisonner, rédiger | oui |
| écrire du code | oui, en généraliste |
| **lire** une image | oui, par le projecteur |
| **fabriquer** une image | non — c'est Flux |
| entendre un son | **non** — c'est Whisper |

La distinction entre lire et fabriquer n'est pas un détail : elle décide de ce que
l'aiguilleur doit envoyer où. « Décris-moi cette photo » va au 9B ; « fais-moi une
photo » va à l'image.

### La règle qui en découle

**Le généraliste est le filet, pas le premier réflexe.** L'aiguilleur décide en
232 ms ; on ne convoque le 9B que lorsque la décision est douteuse ou l'enjeu réel.
Le convoquer systématiquement reviendrait à n'avoir jamais eu d'aiguilleur — et à
payer 848 ms là où 232 suffisaient.
