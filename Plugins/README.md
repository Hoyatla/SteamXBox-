# Plugins SteamXBox

Bibliothèque de modules chargés par `SteamXBox.Desktop`. Un plugin ajoute un comportement ou une
apparence à l'environnement sans qu'il faille recompiler quoi que ce soit.

## La règle qui prime sur toutes les autres : aucune injection

Un plugin **ne s'injecte jamais** dans un autre processus. Pas de DLL dans `explorer.exe`, pas de
détour d'API système, pas de hook global.

Ce n'est pas de la prudence excessive, c'est ce qui sépare ce projet d'un Windhawk : un logiciel qui
injecte se fait signaler par les antivirus, casse à chaque mise à jour cumulative de Windows, et
emporte le shell avec lui quand il plante. SteamXBox agit sur **ses propres fenêtres**, sur les
**réglages que Windows expose officiellement**, et sur rien d'autre.

Conséquence assumée : certains effets ne sont pas réalisables et ne le seront pas. Une animation de
minimisation à la macOS ou un flou de mouvement du curseur système demandent une injection. Sur les
fenêtres de SteamXBox, en revanche, tout est permis puisque c'est lui qui les dessine.

## Ce qu'un plugin peut faire

| Catégorie | Portée | Exemples |
|---|---|---|
| `theme.windows` | Réglages Windows documentés, réversibles | curseurs, couleur d'accentuation, mode sombre, durées d'animation |
| `theme.surface` | Fenêtres de SteamXBox | acrylique, mica, transitions, animations d'ouverture |
| `tile` | Centre de contrôle | raccourci direct vers une information ou un panneau Windows |
| `widget` | Environnement | affichage périodique (batterie, réseau, lecture en cours) |
| `tool` | Un outil du centre de contrôle | calculatrice, presse-papiers, minuteur, convertisseur |

## Les outils sont des plugins

Un outil n'a pas son propre dossier, son propre manifeste ni son propre chargeur. C'est un plugin de
catégorie `tool`, et ce n'est pas une économie de code : deux mécanismes qui font la même chose
divergent, et c'est toujours le second qui reste en retard sur le premier.

Trois règles, et la troisième est celle qui rend les deux autres possibles.

### 1. Un outil est un dossier

Déposé, il est installé. Jeté, il est désinstallé. Rien à recompiler, rien à déclarer ailleurs,
aucune trace laissée derrière. C'est ce que veut dire *débranchable*, et c'est la seule définition
qui se vérifie : si retirer le dossier ne suffit pas, l'outil n'est pas débranchable.

### 2. Un outil ne dépend que de son manifeste

Il ne référence aucun assemblage de SteamXBox, ne partage aucun état avec un autre outil, et ne
suppose la présence de rien d'autre que l'hôte. Un outil qui manque, qui est corrompu ou dont le
manifeste est illisible est ignoré avec une ligne dans le journal — jamais une erreur qui emporte
l'environnement ou les autres outils.

### 3. L'hôte dessine, l'outil décrit

L'outil ne dessine pas sa fenêtre. Il déclare ce qu'il contient ; `SteamXBox.Desktop` fournit la
fenêtre, le thème, la navigation à la manette et le clavier virtuel.

C'est le point le plus contraignant du contrat et le plus important. Dix outils écrits par dix
personnes qui dessinent chacune leur fenêtre donnent dix fenêtres étrangères, dont aucune ne se
pilote à la manette — et une manette est le seul périphérique dont ce projet garantit la présence.
En laissant l'hôte dessiner, un outil écrit par un utilisateur est navigable à la manette sans que
son auteur ait eu à y penser.

Le coût est réel et assumé : un outil ne peut pas avoir une interface que l'hôte ne sait pas décrire.
Le vocabulaire s'étendra à mesure que de vrais outils le demanderont, jamais par anticipation.

## Ce qu'un outil déclaratif peut faire

Le premier niveau ne contient **aucun code** — un `plugin.json` et rien d'autre. Il couvre le cas le
plus fréquent : lancer quelque chose, ouvrir un panneau Windows, afficher une valeur.

```json
{
  "id": "minuteur",
  "name": "Minuteur",
  "category": "tool",
  "version": "1.0.0",
  "licence": "",
  "glyph": "E916",
  "hint": "Compte à rebours simple.",
  "surface": "panel",
  "content": [
    { "kind": "number", "id": "minutes", "label": "Minutes", "min": 1, "max": 180, "value": 5 },
    { "kind": "action", "label": "Démarrer", "does": "start" }
  ]
}
```

`glyph` est un point de code Segoe Fluent Icons, comme `ToolDescriptor.Glyph` aujourd'hui.

`surface` vaut `tile` — l'outil agit sans rien montrer — ou `panel`, et l'hôte ouvre alors une
fenêtre dont il dessine le contenu à partir de `content`.

`does` nomme une action que l'hôte sait exécuter. Un outil déclaratif ne peut donc rien faire que
l'hôte ne sache déjà faire : c'est la limite du niveau, et c'est aussi ce qui garantit qu'un outil
téléchargé ne peut pas dépasser ce que le manifeste montre à qui le lit.

Les deux niveaux suivants — un outil qui calcule, puis un outil dans son propre processus — ne sont
pas décrits ici. Ils le seront quand un besoin réel les demandera, et le champ `entry` existe déjà
pour les recevoir. Figer leur contrat maintenant reviendrait à le deviner à partir de zéro outil.

### La ligne à ne pas franchir

Le projet est en pré-alpha : les outils et le chargeur vont se complexifier, et c'est normal. Une
seule contrainte doit survivre à cette croissance.

`content` et `does` peuvent s'enrichir autant que de vrais outils le demandent — d'autres champs,
d'autres actions. Mais dès qu'un outil a besoin de **calculer** quelque chose que l'hôte ne sait pas
nommer, il passe au niveau suivant ; il ne réclame pas une action `does` de plus.

Sans cette ligne, `does` grossit jusqu'à ce que le manifeste soit un langage et le chargeur son
interprète — avec ses conditions, ses variables, ses boucles, et sa syntaxe que personne n'a conçue.
C'est la fin habituelle des systèmes déclaratifs, et la calculatrice est le premier candidat à la
provoquer : elle évalue une expression, donc elle appartient au niveau deux, quoi qu'il en coûte de
la garder dehors en attendant.

La bonne question devant chaque ajout n'est pas « est-ce possible ? » mais « est-ce que l'hôte sait
déjà faire ça pour lui-même ? ». Si oui, l'exposer est légitime. Sinon, c'est du calcul déguisé.

## Retrouver ses outils d'une session à l'autre

L'environnement rouvre ce qui était ouvert. C'est l'hôte qui rouvre, pas l'outil qui survit, donc
rien de la règle sur les processus invisibles n'est en cause ici.

Un outil déclare ce qu'il veut retrouver :

```json
"remembers": ["minutes", "derniere-unite"]
```

L'hôte écrit ces valeurs dans l'emplacement qu'il alloue à l'outil et les lui rend à l'ouverture
suivante. Nommées une par une, jamais « tout mon état » : ce qui n'est pas nommé n'est pas conservé,
donc un outil ne peut pas faire persister à l'insu de l'utilisateur quelque chose qu'il n'a pas
déclaré.

Un outil doit s'ouvrir correctement quand rien n'a été retenu — première utilisation, valeurs
effacées, outil déplacé sur une autre machine. L'état retrouvé est un confort, jamais une condition
de fonctionnement.

## Ce qu'un outil ne peut pas faire

La règle d'injection s'applique sans exception. S'y ajoutent trois interdits propres aux outils, pour
la même raison qu'ils existent chez les plugins — un utilisateur doit pouvoir installer un outil
trouvé quelque part sans avoir à en lire le code.

Chacun est écrit avec ce qu'il **n'interdit pas**, parce que la première rédaction de cette section
interdisait par accident d'enregistrer un document et d'ouvrir le Bloc-notes.

### Écrire sur le disque de sa propre initiative

Un outil écrit dans son dossier et dans l'emplacement que l'hôte lui alloue. Il ne va pas déposer
des fichiers ailleurs sans que personne l'ait demandé.

**Ce qui reste permis, et qui est le cas normal :** enregistrer là où l'utilisateur a choisi. L'outil
demande à l'hôte d'ouvrir le panneau d'enregistrement ; l'utilisateur désigne un fichier ; l'hôte
accorde l'accès **à ce fichier-là**. L'outil ne voit jamais l'arborescence, seulement ce qui lui a
été désigné.

C'est le modèle du *powerbox* de macOS, et il tombe juste ici pour une raison qu'on a déjà : c'est
l'hôte qui dessine. Le panneau d'enregistrement est le sien, donc il est navigable à la manette, il
suit le thème, et il est le seul endroit d'où un chemin peut sortir. Un outil déclaratif obtient tout
cela sans que son auteur y ait pensé.

**Un dossier se désigne de la même façon.** Un outil qui doit voir plusieurs fichiers — trier des
photos, renommer une série, faire relire un dossier par une IA locale — demande une autorisation de
dossier. L'utilisateur en désigne un dans le panneau de l'hôte ; l'outil obtient ce sous-arbre, et
rien au-dessus.

L'autorisation déclare sa portée : **lecture**, ou **lecture et écriture**. Un outil qui veut
seulement lire ne demande pas le droit de déplacer, et l'utilisateur voit lequel des deux on lui
demande avant de désigner quoi que ce soit.

Deux exigences s'y ajoutent, parce qu'une autorisation de dossier ne ressemble pas à une
autorisation de fichier : elle porte sur des fichiers que personne n'a énumérés, et une réorganisation
en touche des dizaines d'un coup.

- **Le plan avant l'exécution.** Un outil qui modifie un sous-arbre annonce ce qu'il va faire —
  quoi est déplacé, renommé, réécrit — et l'hôte le montre. L'utilisateur approuve une liste, pas une
  intention. C'est la seule forme de consentement qui veuille dire quelque chose quand l'auteur du
  plan est une IA à laquelle personne ne peut demander de comptes.
- **De quoi revenir en arrière.** L'hôte garde ce qu'il faut pour défaire l'opération. Même
  exigence que `revertible` chez les plugins `theme.windows`, et pour la même raison : ce qui touche
  aux affaires de l'utilisateur doit pouvoir être annulé par lui.

### Laisser tourner quelque chose d'invisible

Un outil ne laisse pas derrière lui de processus en tâche de fond, de tâche planifiée ni d'entrée de
démarrage. Quitter SteamXBox doit suffire à ce qu'il ne reste rien de lui qui tourne.

**Ce qui reste permis :** lancer une application. Ouvrir le Bloc-notes, un panneau `ms-settings:`,
l'explorateur, un navigateur — et cette application survit à SteamXBox, comme il se doit : c'est la
fenêtre de l'utilisateur, pas un résidu de l'outil. La distinction est la visibilité, pas la durée de
vie.

### Lire une manette directement

Les manettes appartiennent au Core, qui les distribue. Un outil qui en ouvrirait une lui-même la
volerait à tout le reste — au bridge, au clavier virtuel, aux autres joueurs.

**Ce qui reste permis :** tout, par l'hôte. Un outil dessiné par l'hôte est navigable à la manette
sans avoir à la lire, ce qui est l'intérêt entier de la règle « l'hôte dessine ».

### Toucher au système

Installer ou mettre à jour un pilote, écrire dans le registre hors des réglages documentés et
réversibles, modifier un service : jamais depuis un outil, à aucun niveau.

Ce n'est pas de l'injection, donc la règle d'or ne l'attrape pas — d'où cette ligne. Un pilote exige
l'élévation, modifie le système et ne se défait pas ; c'est l'inverse exact de ce que `revertible`
impose déjà aux plugins `theme.windows`. Un outil téléchargé quelque part qui peut installer un
pilote est précisément la porte que ce format existe pour fermer.

SteamXBox le fait pour lui-même, par ses installeurs HidHide et ViGEmBus, avec le consentement
explicite de l'utilisateur et sous son propre nom. C'est une capacité de l'hôte, et elle ne descend
pas dans le manifeste.

### Lire, en revanche, n'est jamais restreint

Interroger l'index de recherche Windows, lire un fichier que l'utilisateur a désigné, consulter une
information système : rien de tout cela n'est visé. La seule limite du premier niveau est son
vocabulaire — un outil déclaratif ne peut faire que ce que l'hôte sait déjà faire — et elle est
volontaire, pas défensive.

## Structure

```
Plugins/
  <identifiant>/
    plugin.json        obligatoire
    ...                ressources propres au plugin
```

## plugin.json

```json
{
  "id": "layan-cursors",
  "name": "Curseurs Layan White",
  "category": "theme.windows",
  "version": "1.0.0",
  "author": "",
  "licence": "GPL-3.0",
  "revertible": true,
  "entry": "cursors/install.inf"
}
```

`revertible` n'est pas décoratif. Tout plugin qui écrit dans Windows doit déclarer `true` et
**sauvegarder l'état d'origine avant de l'écraser**. Sans cela, désinstaller SteamXBox laisserait un
système avec des curseurs et des couleurs orphelins que l'utilisateur ne saurait pas d'où ils
viennent. Un plugin `theme.windows` non réversible est refusé au chargement.

`licence` est obligatoire pour tout plugin embarquant des ressources tierces — curseurs, icônes,
polices. SteamXBox est distribué en version gratuite **et** payante ; une ressource dont la licence
interdit l'usage commercial ne peut pas être livrée avec, et le champ est ce qui permet de le
vérifier au lieu de le supposer.

## État

Le chargeur n'est pas encore écrit. Ce document fixe le contrat auquel il devra se tenir ; le
premier plugin sera `theme.windows` avec les curseurs Layan White et le mécanisme de sauvegarde et
restauration, sur lequel tous les suivants s'appuieront.

Côté outils, la calculatrice et le presse-papiers sont compilés dans l'environnement et décrits par
`ToolDescriptor`, dont la forme suit déjà celle d'un manifeste. Ils restent la référence : le
chargeur déclaratif sera considéré comme juste le jour où il produira la même tuile qu'eux à partir
d'un `plugin.json` seul, sans que rien d'autre change dans l'environnement.
