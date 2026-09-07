# Plan de développement — les langages de l'Atelier

**But : SenSÉ embarque ses langages.** L'Atelier doit pouvoir exécuter le code
qu'il écrit, sans dépendre de ce que la machine de l'utilisateur contient — comme
il embarque déjà Python, `ffmpeg` et `llama.cpp`.

Le langage se choisit de deux façons : **l'utilisateur le fixe dans le nœud**, ou
**le modèle le décide** quand personne ne l'a demandé.

Toutes les mesures de ce document ont été relevées le 8 septembre 2026 sur la
machine de référence. Aucune n'est estimée.

---

## 1. Le défaut à corriger

**L'Atelier écrit du code en douze langages et n'en exécute qu'un.**

`llm_generer_code` (`Bibliotheque/Codage/Llm.cs`) propose `python, rust,
javascript, typescript, csharp, cpp, c, go, java, kotlin, swift, shell`. Le seul
nœud d'exécution est `executer_python` (`Primitives.cs`, n° 8).

Un graphe peut donc produire du Rust et n'a aucun moyen de l'essayer. Le modèle
écrit dans le vide, et l'utilisateur n'a que ses yeux pour juger.

---

## 2. Les mesures

### Ce que coûte une exécution

« Hello world », du lancement à la sortie lue, moyenne à froid :

| Langage | Comment | Latence |
|---|---|---|
| **python** | interprété | **210 ms** |
| **node** | interprété | **342 ms** |
| **java** | source directe — `java h.java` | **548 ms** |
| **c** | `gcc h.c && h.exe` | **1 507 ms** |
| **rust** | `rustc -O h.rs && hr.exe` | **1 694 ms** |
| **csharp** | fichier seul — `dotnet run h.cs` | **2 610 ms** |

Un facteur **douze** entre le premier et le dernier. Ce n'est pas un détail de
confort : un graphe qui appelle dix fois un nœud paie deux secondes en Python et
vingt-six en C#.

Deux découvertes qui simplifient beaucoup :

- **Java exécute un fichier source directement** depuis JDK 11 — pas de
  compilation explicite, pas de projet.
- **C# aussi**, depuis .NET 10 : `dotnet run h.cs` sur un fichier isolé, sans
  `.csproj`. La machine de référence porte 10.0.301, donc c'est disponible
  aujourd'hui.

Deux langages réputés lourds se comportent donc comme des scripts. Seuls C et
Rust exigent une vraie chaîne de compilation.

### Ce que coûte un moteur à embarquer

| Moteur | Sur le disque | Remarque |
|---|---|---|
| **python 3.11** | **301 Mo** | déjà embarqué, `Outils/Python` |
| **node 24** | **101 Mo** | le plus léger |
| **java (JDK 17)** | 291 Mo | `jlink` sait le réduire à ~60 Mo |
| **.NET runtime seul** | 74 Mo | mais ne compile pas |
| **.NET SDK** | **2 907 Mo** | nécessaire pour `dotnet run fichier.cs` |
| **gcc (MinGW-W64)** | **914 Mo** | C et C++ |
| **rust (rustup + cargo)** | **4 571 Mo** | 2 817 + 1 754 |

**Tout embarquer coûterait environ neuf gigaoctets**, dont la moitié pour Rust
seul. C'est la décision centrale de ce plan, et elle ne se prend pas au flair.

---

## 3. Les trois rangs

Un moteur n'a pas à être traité comme les autres selon qu'il pèse cent mégaoctets
ou quatre gigaoctets. D'où trois rangs, et un seul mécanisme pour les distinguer.

### Rang 1 — embarqués, livrés avec le produit

**python (301 Mo) + node (101 Mo) = 402 Mo.**

Les deux sont interprétés, les deux répondent sous 350 ms, et à eux deux ils
couvrent l'essentiel de ce qu'un nœud de graphe fait : lire, transformer,
appeler, calculer. Ils existent sur **toute** machine où SenSÉ est installé, donc
un graphe qui s'en sert est portable — c'est la seule garantie qui compte quand on
partage un graphe.

Node apporte en plus TypeScript et JavaScript, deux entrées de la liste que
`llm_generer_code` propose déjà.

### Rang 2 — embarquables à la demande

**java, csharp, c/c++.**

Téléchargés au premier usage, sur le modèle exact de `ChromiumEmbarque.cs`
(`src/SenSÉ.Mcp.Bus/`) : révision épinglée, copie en `.part`, installation dans
`Outils/`. L'utilisateur n'installe rien à la main, mais ne paie pas non plus
trois gigaoctets pour un langage qu'il n'ouvrira jamais.

`jlink` doit être employé pour Java : il produit une image de ~60 Mo au lieu de
291, et cela suffit largement à `java fichier.java`.

Pour C#, il faut le **SDK** et non le runtime : `dotnet run fichier.cs` compile.
Deux gigaoctets neuf cents, à télécharger seulement si quelqu'un le demande.

### Rang 3 — détectés, jamais livrés

**rust, go, et tout ce qui viendra.**

4,5 Go pour Rust est hors de proportion avec le service rendu dans un graphe. S'il
est présent sur la machine, on s'en sert ; sinon on ne le propose pas. C'est aussi
la porte ouverte pour tout langage qu'on n'a pas prévu.

**Un moteur absent n'est jamais déclaré.** C'est la règle déjà appliquée aux
recherches distantes non configurées et au mode interactif de l'Assistant : un
verbe qui échoue à chaque appel coûte un tour et apprend au modèle à s'entêter.

---

## 4. Le manifeste de moteur

Un fichier par langage, dans `Outils/Langages/<id>/moteur.json`, sur le modèle
exact des manifestes de modèles (`Outils/Modeles/*/modele.json`) :

```json
{
  "id": "node",
  "nom": "JavaScript / TypeScript (Node 24)",
  "espace": "Codage",
  "rang": "embarque",
  "extensions": [".js", ".mjs", ".ts"],
  "detection": "node --version",
  "executable": "node/node.exe",
  "commande": ["{executable}", "{fichier}"],
  "compile": false,
  "latence_ms": 342,
  "taille_mo": 101
}
```

Pour un langage compilé, deux commandes :

```json
{
  "id": "rust",
  "rang": "detecte",
  "compile": true,
  "commande_compilation": ["rustc", "-O", "{fichier}", "-o", "{sortie}"],
  "commande": ["{sortie}"],
  "latence_ms": 1694,
  "taille_mo": 4571
}
```

`latence_ms` et `taille_mo` ne sont pas de la décoration : ce sont les deux
chiffres dont le modèle a besoin pour choisir (voir §6), et ceux que l'interface
doit montrer avant un téléchargement de trois gigaoctets.

**Les chemins sont relatifs à `Outils/Langages/`.** Même raison que pour les
modèles : `C:\Program Files\SenSÉ` porte un accent, et certains binaires natifs
ne savent pas l'ouvrir — voir `PLAN-MOTEUR.md` §4a, où le problème est documenté
et résolu par le répertoire courant.

---

## 5. Le nœud `executer_code`

**Un seul nœud, le langage en paramètre.** Douze nœuds qui se ressembleraient
seraient douze fois la même faute à corriger.

```
executer_code
  entrées    : code (texte)
  sorties    : stdout, stderr, code_retour, duree_ms
  paramètres : langage (liste, alimentée par les moteurs disponibles)
               entrees (fichiers mis à disposition, optionnel)
               limite_s (défaut 30)
```

`executer_python` disparaît, absorbé — son nom promettait un langage là où le
graphe en mérite douze.

**La liste `langage` est alimentée à l'exécution**, jamais écrite en dur : elle
contient ce que la détection a trouvé, plus `auto`. Un graphe partagé qui demande
un moteur absent doit le dire clairement — « ce graphe demande Rust, absent de
cette machine » — et non échouer sur une erreur de processus.

`code_retour` et `duree_ms` en sortie parce qu'un graphe doit pouvoir brancher sur
l'échec, et parce qu'un nœud qui prend deux secondes et demie doit pouvoir le
dire à celui qui l'ordonnance.

---

## 6. Qui choisit le langage

### L'utilisateur, quand il le veut

Le paramètre `langage` du nœud. C'est le cas simple et il l'emporte toujours :
**un choix explicite n'est jamais réinterprété.**

### Le modèle, quand personne n'a demandé

`langage: "auto"`. Le modèle reçoit la liste des moteurs disponibles avec leur
`latence_ms`, et une règle courte :

> Choisis le moteur le moins cher qui sache faire la tâche. À capacité égale,
> celui qui répond le plus vite. Ne demande pas un moteur absent de la liste.

Cette règle tient en trois lignes de consigne et évite le travers observé
ailleurs dans ce produit : un modèle qui, faute de savoir ce qui est disponible,
essaie tout à la file et remplit son contexte d'échecs.

**Le choix doit être écrit dans le graphe après coup**, pas laissé implicite. Un
graphe rejoué doit refaire la même chose : `auto` se résout une fois, et le
langage retenu est inscrit dans le nœud. Sans cela, le même graphe donnerait un
résultat différent d'une machine à l'autre — et c'est exactement le genre de
défaut qu'on ne voit qu'une fois le graphe partagé.

### Ce que le modèle n'a pas à décider

Le téléchargement d'un moteur de rang 2. Trois gigaoctets se demandent à
l'utilisateur, comme le produit demande déjà confirmation pour installer un
logiciel. Le modèle propose, l'environnement dessine la question.

---

## 7. La décision qui n'est pas prise

**Un nœud qui exécute du code arbitraire est le point le plus sensible du
produit.**

Le nœud actuel écrit un fichier dans `%TEMP%` et le lance sans autre borne qu'un
délai de trente secondes : pas de limite de réseau, de disque, ni de mémoire. Le
multiplier par douze multiplie aussi cela par douze.

La règle de SenSÉ s'y applique en entier — *rien ne s'exécute que l'utilisateur
n'aurait pu lancer lui-même* — mais elle ne dit pas encore ce qu'un tel nœud a le
droit de faire. Il faut trancher, **avant** d'écrire le premier moteur de rang 2 :

- Le réseau : autorisé, refusé, ou demandé ?
- Le disque : un dossier de travail à lui, ou tout le disque de l'utilisateur ?
- La durée : trente secondes suffisent-elles quand Rust compile ?
- L'exécution est-elle un acte qui demande confirmation, comme installer un
  logiciel — ou un acte ordinaire, comme ouvrir un fichier ?

Ce document ne tranche pas. Il refuse seulement de faire semblant que la question
n'existe pas.

---

## 8. Les étapes

### Étape 1 — le socle, sans rien télécharger

Le manifeste, la détection, le nœud `executer_code`, la liste alimentée à
l'exécution. Python est déjà là et sert de premier moteur ; les moteurs présents
sur la machine (rang 3) se détectent et s'ajoutent gratuitement.

**Fini quand :** un graphe exécute du Python et, sur cette machine, du Rust —
sans qu'une ligne de code nomme Rust. La liste du nœud montre ce qui est là et
rien d'autre.

### Étape 2 — Node embarqué

101 Mo, deux entrées de plus dans la liste (JavaScript, TypeScript), aucun
téléchargement à l'usage. C'est le meilleur rapport de tout ce plan.

**Fini quand :** `Outils/Langages/node/` existe, le graphe l'utilise, et une
machine neuve sans Node installé l'exécute quand même.

### Étape 3 — le rang 2, à la demande

L'installateur sur le modèle de `ChromiumEmbarque.cs`, puis Java par `jlink`, puis
.NET, puis MinGW. Dans cet ordre : du plus léger au plus lourd.

**Fini quand :** demander un langage absent propose son installation, dit ce
qu'elle pèse, et fonctionne après acceptation — sans redémarrer SenSÉ.

### Étape 4 — le choix automatique

`langage: "auto"`, la règle de §6 dans la consigne, et la résolution inscrite dans
le graphe.

**Fini quand :** deux exécutions du même graphe choisissent le même langage, et
qu'un graphe emporté sur une autre machine se comporte pareil ou dit pourquoi il
ne peut pas.

---

## 9. Ce qui se teste sans aucun moteur installé

La suite de tests du produit tourne sans Rust, sans .NET SDK et sans MinGW. Doit
donc être vérifiable à vide :

- La lecture des manifestes et la validité de leurs chemins.
- La détection : un moteur absent n'apparaît pas dans la liste du nœud.
- La construction de la ligne de commande, y compris les substitutions
  `{executable}`, `{fichier}`, `{sortie}`.
- La résolution de `auto` : sur une liste de moteurs simulée, la règle choisit
  bien le moins cher qui convient, et inscrit son choix.
- Le refus propre : un graphe qui demande un moteur absent produit un message
  nommant le langage, pas une exception de processus.

Ce qui exige les moteurs — l'exécution elle-même — se vérifie à la main, avec les
latences du §2 comme référence.

---

## Annexe — l'état de la machine de référence, 8 septembre 2026

| Moteur | Présence | Version |
|---|---|---|
| python | **embarqué dans le produit** | 3.11.0 |
| dotnet | machine | 10.0.301 |
| node | machine | 24.18.0 |
| cargo / rustc | machine | 1.98.0 |
| java | machine | 17.0.12 LTS (et JDK 22) |
| gcc | machine | MinGW-W64 UCRT 16.1.0 |
| go, pwsh | absents | — |

Ces versions décrivent **une** machine, pas le produit. C'est précisément la
raison de ce plan : ce qui est embarqué existe partout, le reste est un hasard
heureux.
