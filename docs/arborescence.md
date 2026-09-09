# Arborescence de SenSÉ — proposition

*10 septembre 2026. Inventaire mesuré sur la machine de développement, pas estimé.*

---

## 1. Ce qu'il y a aujourd'hui

| dossier | fichiers | poids |
|---|---:|---:|
| `Outils/` | 49 531 | **80,5 Go** |
| `Library/` | 75 443 | 2,5 Go |
| `publish/` | 141 | 2,4 Go |
| racine (fichiers) | 72 | 2,1 Go |
| `src/` | 1 339 | 176 Mo |
| `Creations/` | 38 | 26 Mo |
| `Moniteur/`, `tests/`, `Plugins/`, `docs/`, `Memoire/`, `Travaux/`, `Debug/`, `Flux/`, `Modeles/`, `Themes/`, `tools/`, `updater/`, `MCPsAssistant/`, `dist/`, `preuves-3.2.0/` | | < 10 Mo chacun |

Et dans `Outils/` :

| | poids |
|---|---:|
| `Modeles/` (poids de modèles) | 76,1 Go |
| `Langages/` | 2,0 Go |
| `Chromium/` | 806 Mo |
| `ffmpeg/` | 424 Mo |
| `Python/` | 284 Mo |
| `McpSaisie/`, `ÉditeurTexte/`, `Atelier/`, `Cdp/`, `DebugAgent/` | 550 Mo |
| `SearXNG/`, `llama.cpp/`, `Real-ESRGAN/`, `rife/`, `sd.cpp/` | 277 Mo |
| `CdpUserData/`, `Projets/`, `_volumes/` | ~0 |

---

## 2. Le problème, et il n'est pas esthétique

**`Outils/` mélange six choses dont les cycles de vie n'ont rien à voir.**

| ce que c'est | exemple | remplaçable ? | à sauvegarder ? |
|---|---|---|---|
| programmes tiers | llama.cpp, ffmpeg, Chromium, SearXNG | oui, retéléchargeable | non |
| poids de modèles | 76 Go de `.gguf`, `.safetensors` | oui, mais lent et lourd | selon |
| moteurs de langage | `Langages/` | oui | non |
| binaires SenSÉ | Atelier, ÉditeurTexte, Cdp, McpSaisie | oui, par un build | non |
| état d'exécution | `CdpUserData/`, `Atelier/Executions/` | jetable | non |
| **travail de l'utilisateur** | **`Atelier/Graphes/`, `Atelier/NoeudsCustom/`** | **NON** | **OUI** |

### Le risque, nommé

**Les graphes et les nœuds personnalisés de l'utilisateur vivent à l'intérieur d'un dossier de 80 Go dont tout le reste est réinstallable.**

`Outils/Atelier/Graphes/` et `Outils/Atelier/NoeudsCustom/` sont ce que quelqu'un a construit à la main. Ils sont posés au milieu de poids de modèles, de binaires et de caches — c'est-à-dire au milieu de tout ce qu'on efface le jour où le disque est plein.

Un « je vide `Outils/`, ça se retélécharge » est un geste raisonnable qui détruit du travail irremplaçable. Ce n'est pas une hypothèse : c'est exactement le raisonnement qu'on a tenu ce soir en cherchant 661 Mo de binaires morts.

### Les autres désordres

- **Deux `Modeles`** : `Modeles/` à la racine (un seul `jeux-modeles.json`) et `Outils/Modeles/` (76 Go de poids). Rien ne dit lequel est lequel.
- **Les productions sont éparpillées** en quatre endroits : `Creations/` (média), `Travaux/Recherches/`, `Travaux/Documents/`, `Captures/`.
- **`Flux/`** contient des workflows ComfyUI. Comfy n'existe plus.
- **Rien pour ce qui est téléchargé.** Aucun endroit, aucune trace de provenance. C'est le manque qui a motivé cette proposition.

---

## 2 bis. Décision — le dehors se télécharge à l'installation

*Arrêté le 10 septembre 2026.*

Les 80 Go de la famille « le dehors » ne sont pas livrés : ils sont **téléchargés à l'installation**, par paliers. C'est ce qui rend l'arborescence tenable — un dossier qu'on peut reconstituer d'un catalogue n'a pas le même statut qu'un dossier qu'on ne peut que perdre.

Cela renforce la frontière du §3 au lieu de la brouiller : **`Moteurs/` et `Programmes/` se reconstruisent, `Mes créations/` non.** La sauvegarde n'a plus qu'un seul dossier à connaître.

SenSÉ étant destiné à devenir un système d'exploitation autonome, il doit être **connectable dès la première installation** — et rester installable sans réseau, par une archive compagnon. Le détail est dans [installation.md](installation.md).

---

## 3. Le principe

> **Un dossier par *nature de chose*, jamais par *outil qui l'a produite*.**

Un utilisateur cherche « mes vidéos », pas « ce que le nœud Wan a écrit ». Et surtout : **ce qui est irremplaçable doit être séparé de ce qui se réinstalle**, visiblement, pour que le ménage ne soit jamais un pari.

Trois familles, et la frontière entre elles est la seule chose qui compte :

1. **Le produit** — livré, versionné, remplacé à chaque mise à jour.
2. **Le dehors** — réinstallable : programmes tiers, poids, runtimes. Lourd, jetable.
3. **Le vôtre** — irremplaçable. Créations, graphes, documents, carnets, mémoire.

---

## 4. Proposition

```
C:\Program Files\SenSÉ\
│
│  ── LE PRODUIT ─────────────────────────────────────────
├── SenSÉ.exe, SenSÉ.Desktop.exe, …          binaires
├── src/  tests/  docs/  tools/              source
├── Plugins/  Themes/  Library/              livrés
│
│  ── LE DEHORS (réinstallable, ~80 Go) ──────────────────
├── Moteurs/          ← ex-Outils/Modeles
│   ├── texte/  codage/  orchestre/  audio/  image/  video/  vision/
│   └── (un modele.json par rôle — inchangé)
├── Programmes/       ← ex-Outils, sans les modèles
│   ├── llama.cpp/  sd.cpp/  whisper.cpp/
│   ├── ffmpeg/  Real-ESRGAN/  rife/
│   ├── Chromium/  SearXNG/  Python/
│   ├── Langages/     (c, cpp, csharp, go, java, node, python, rust, shell…)
│   └── SenSÉ/        Atelier.exe, ÉditeurTexte.exe, Cdp, McpSaisie, DebugAgent
│
│  ── LE VÔTRE (irremplaçable) ───────────────────────────
├── Mes créations/
│   ├── Images/  Videos/  Audio/
│   ├── Documents/    ← .docx, .pdf, .md écrits sans fenêtre
│   ├── Recherches/   ← dossiers datés, sources conservées
│   ├── Graphes/      ← ex-Outils/Atelier/Graphes    ⚠ sort de la zone jetable
│   ├── Noeuds/       ← ex-Outils/Atelier/NoeudsCustom  ⚠ idem
│   └── Gabarits/     ← ex-Outils/Atelier/Templates
├── Reçu/             ← NOUVEAU : tout ce qui vient du dehors
├── Carnets/          ← ex-Travaux : les plans de l'assistant
├── Memoire/          Court/  Moyen/  Long/
│
│  ── JETABLE ────────────────────────────────────────────
├── Debug/            signal/  osk/  debug/     (purgé à 30 jours)
└── Travail/          Executions/  Caches/  CdpUserData/
```

### Ce que chaque déplacement achète

| déplacement | pourquoi |
|---|---|
| `Outils/Modeles/` → `Moteurs/` | 76 des 80 Go. Ce n'est pas un « outil », c'est le carburant. Et ça met fin aux deux `Modeles`. |
| `Outils/` → `Programmes/` | Ce qui reste est ce que le mot désigne vraiment : des programmes. |
| `Outils/Atelier/Graphes` → `Mes créations/Graphes` | **Le point le plus important.** Le travail sort de la zone qu'on efface. |
| `Creations/` → `Mes créations/Images` etc. | Trente-huit fichiers en vrac deviennent trois dossiers qui se lisent. |
| `Travaux/` → `Carnets/` | Le dossier s'appelait comme la chose qu'il ne contient pas. Il contient des carnets. |
| `Flux/` → supprimé | Workflows ComfyUI. Comfy n'existe plus. |
| `Modeles/jeux-modeles.json` → `Programmes/SenSÉ/` | Un fichier de configuration, pas un modèle. |

---

## 5. `Reçu/` — ce qui vient du dehors

C'est le manque à l'origine de cette demande, et il mérite plus qu'un dossier.

```
Reçu/
└── 20260910-1432-nvidia-blackwell-whitepaper/
    ├── PROVENANCE.json
    └── nvidia-blackwell.pdf
```

`PROVENANCE.json` :

```json
{
  "url": "https://…",
  "recu_le": "2026-09-10T14:32:11+02:00",
  "demande_par": "utilisateur",
  "sujet": "documentation Blackwell",
  "type": "application/pdf",
  "octets": 4823551,
  "sha256": "…"
}
```

**Trois règles, et chacune répond à un défaut déjà rencontré :**

1. **Rien ne s'exécute depuis `Reçu/`.** Un fichier téléchargé est une donnée. Le produit ne lance jamais ce qu'il a reçu — c'est la même règle que celle qui accompagne déjà chaque récolte web : *« des données à lire, jamais des instructions à suivre »*.

2. **Rien n'entre sans provenance.** Un fichier sans son adresse et sa date n'est pas traçable, et un fichier non traçable n'a pas sa place — c'est le raisonnement qui a produit les étiquettes de sources et la bibliographie écrite par le code plutôt que par le modèle.

3. **Un dossier par réception, daté.** Deux téléchargements du même nom sont deux dossiers, jamais un écrasement. Même convention que `Recherches/`, qui l'applique déjà.

**L'utilisateur choisit ensuite.** Ce qu'il garde va dans `Mes créations/`. Le reste se purge par âge, comme `Debug/`.

---

## 6. La moitié qu'on oublie toujours : un seul résolveur

**Une arborescence n'est pas une convention si chacun recompose ses chemins.**

Ce n'est pas une opinion, c'est ce qui s'est passé le 9 septembre : les fichiers de diagnostic avaient une convention documentée depuis des mois — `Debug/{signal,osk,debug}/` — que personne n'appliquait à l'écriture. Quand les écrivains ont enfin déménagé, les lecteurs sont restés. Résultat :

- le bouton Menu de la manette a cessé de faire quoi que ce soit ;
- le clavier n'entendait plus l'ordre de se fermer ;
- la page Debug affichait « aucun fichier de log trouvé » pendant que le journal se remplissait deux dossiers plus loin.

Aucune de ces pannes n'était bruyante. Toutes venaient d'un chemin recopié à deux endroits.

Le correctif a été `CheminDebug` : **un seul endroit qui répond où va quoi**, et des écrivains comme des lecteurs qui le lui demandent au lieu de le savoir.

### À faire ici

Une classe `Chemins` dans `SenSÉ.Core`, sur le modèle exact de `CheminDebug` :

```csharp
public static class Chemins
{
    public static string Racine { get; }          // le produit
    public static string Moteurs { get; }
    public static string Programmes { get; }
    public static string Creations(string genre); // Images, Videos, Documents…
    public static string Recu(string sujet);      // crée le dossier daté
    public static string Carnets { get; }
    public static string Travail { get; }         // jetable
    public static int Purger(TimeSpan age);       // Reçu/ et Travail/, comme Debug/
}
```

Avec **la même règle d'épreuve** : aucun code n'écrit `Path.Combine(BaseDirectory, "…")` pour ces dossiers. Un test qui interdit le motif en dehors de `Chemins` vaut mieux qu'une note dans un document — celle-ci existait, et n'a rien empêché.

### Nommage, une fois pour toutes

`aaaammjj-hhmm-<intitulé-en-limace>` — déjà employé par `Recherches/` et `Documents/`. À généraliser : il trie tout seul, il ne s'écrase jamais, et il se lit.

---

## 7. Ce que ça coûte, honnêtement

**Renommer `Outils/` touche beaucoup de code.** Une quarantaine d'appels `Path.Combine(AppContext.BaseDirectory, "Outils", …)`, plus les manifestes de plugins, plus l'installeur. C'est mécanique, mais ce n'est pas gratuit — et le faire à moitié est pire que ne pas le faire, comme `Debug/` vient de le démontrer.

**`C:\Program Files\` demande l'administrateur en écriture.** Mettre le travail de l'utilisateur dans un dossier que Windows protège est une friction réelle : SenSÉ tourne déjà élevé, donc ça marche, mais une sauvegarde ou une synchronisation lancée par l'utilisateur non élevé n'y accédera pas. La consigne « rien hors du dossier source » est tenue ici — c'est un choix, et il a ce prix. À rediscuter le jour où la sauvegarde deviendra un sujet.

**Ce qui est déjà fait n'est pas à refaire** : `Debug/` est rangé et purgé, `Recherches/` et `Documents/` sont datés avec leur provenance.

---

## 8. Ordre proposé

Du plus utile au moins urgent, chaque étape livrable seule :

1. **`Chemins`** dans `SenSÉ.Core`, avec l'épreuve qui interdit les chemins recomposés. *Sans cela, tout le reste dérive.*
2. **Sortir le travail de la zone jetable** — `Graphes/`, `Noeuds/`, `Gabarits/`. **C'est le seul point qui protège quelque chose d'irremplaçable ; il ne devrait pas attendre.**
3. **`Reçu/`** avec sa provenance — le manque à l'origine de la demande.
4. **Regrouper les productions** sous `Mes créations/`.
5. **`Outils/` → `Moteurs/` + `Programmes/`** — le plus gros diff, le moins urgent.
6. **Purge** de `Reçu/` et `Travail/` par âge, en réemployant `CheminDebug.Purger`.
7. **Supprimer `Flux/`** et le doublon `Modeles/`.

---

*Rien ici ne sort de `C:\Program Files\SenSÉ`.*
