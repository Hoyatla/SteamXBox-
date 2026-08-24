# SteamXBox

*Texte de présentation — version 0.5.0*

---

## En une phrase

SteamXBox transforme une manette de jeu en une façon d'utiliser Windows.

---

## Le problème

Une manette est le périphérique le plus répandu du salon, et le plus inutile dès qu'on
quitte un jeu. Elle pilote parfaitement un personnage, et elle ne sait pas ouvrir un
dossier, écrire un mot de passe ou lancer une application.

Les solutions existantes traitent le symptôme : elles émulent une souris. Le pointeur
avance, mais rien n'est pensé pour la manette — ni les cibles, ni la saisie de texte, ni
la navigation. On se retrouve à viser des cases de douze pixels avec un joystick.

SteamXBox part de l'autre bout : **si la manette est le seul périphérique garanti, alors
c'est l'environnement qui doit être conçu pour elle.**

---

## Ce que c'est aujourd'hui

Quatre couches, qui s'utilisent séparément ou ensemble.

### 1. Le pont manette

Le point de départ historique. Une manette Steam Controller est exposée aux jeux comme
une manette Xbox 360 virtuelle, via ViGEmBus.

L'idée directrice est la **continuité** : tant que Steam tourne, Steam Input garde la
main sur la manette. Dès que Steam se ferme, SteamXBox prend le relais et la manette
continue de servir. L'utilisateur ne fait rien, ne bascule rien.

### 2. Les manettes comme entrées indépendantes

Steam Controller, DualSense, manettes Xbox — plusieurs à la fois si besoin.

Chaque périphérique physique est une entrée **indépendante**. Les manettes ne s'annulent
pas entre elles : celle qui bouge le pointeur, le bouge. La souris et le clavier ne sont
jamais lus ni gênés.

Chaque manette conserve ses propres réglages — zones mortes, courbes, vitesse du
pointeur, placement du clavier — attachés à une **identité durable** du périphérique. Une
manette est reconnue comme elle-même en se rebranchant, y compris parmi plusieurs
exemplaires du même modèle. L'utilisateur crée ses profils dans l'interface ; ils
s'appliquent à chaque reconnexion.

### 3. Le clavier à l'écran

Deux surfaces, une pilotée aux joysticks, une aux pavés tactiles. Chacune découpe l'entrée
en quatre quadrants qui correspondent aux quatre zones du clavier.

Le détail qui compte : **les bords des pavés correspondent aux bords des zones**. Sans ça,
atteindre un caractère de coin demande de sortir du pavé avec le pouce. C'est le genre de
chose qui ne se voit pas sur une capture d'écran et qui décide si l'outil est utilisable
une heure durant.

Le clavier se place en bas au centre de l'écran, ou flotte près du curseur — à sa gauche,
au-dessus, ou en dessous quand il n'y a plus de place, puisque la droite est l'endroit où
le texte va s'écrire. Retour haptique au survol et à l'appui. Le choix est par manette.

### 4. L'environnement

Un plein écran fenêtré qui tient un centre de contrôle : sortie audio, Wi-Fi, Bluetooth,
luminosité, ne pas déranger, gestionnaire des tâches, capture d'écran, calculatrice,
presse-papiers. Chaque tuile est atteignable aux flèches, donc à la manette.

Ce n'est pas une fenêtre parmi d'autres : c'est un **fond**. Il recouvre le bureau Windows
et reste sous toutes les autres fenêtres, pour qu'un outil lancé depuis lui ne soit jamais
enterré par lui.

Deux gestes globaux :

| Geste | Effet |
|---|---|
| `Maj Maj` | ouvre le lanceur de recherche |
| `§ §` | vide l'écran ; encore une fois, les fenêtres reviennent |

### Le lanceur de recherche

Deux appuis sur Maj, et on tape. Applications, entrées du menu Démarrer, applications du
Store, dossiers, lecteurs connectés. Entrée ouvre, Ctrl+Entrée montre l'élément dans son
dossier. Ce qu'on ouvre est retenu et remonte dans le classement.

Les lecteurs connectés sont rappelés au-dessus du champ et deviennent cherchables **dès le
branchement** ; les amovibles s'éjectent de là.

Trois raccourcis de saisie :

- `D, steamxbox` — restreint la recherche au lecteur D
- `web: <mots>` — recherche web
- `docs: <mots>` — recherche dans les documents de l'organisation

---

## Recherche web et documentaire : la partie qui intéresse les institutions

Les deux sont optionnelles, désactivées tant qu'elles ne sont pas configurées, et surtout
**hébergées par le client, pas par nous**.

**Le web passe par une instance SearXNG** — logiciel libre, auto-hébergé, que
l'administrateur installe et contrôle. Rien ne part vers un moteur que l'établissement
n'a pas choisi. Pas de compte, pas de traçage, pas de tiers dans la boucle.

**Les documents passent par une instance Meilisearch** contenant le corpus de
l'organisation, construit par un indexeur livré avec le produit, depuis un dossier ou un
partage réseau. Il lit le texte brut, le PDF, Word, Excel, PowerPoint — les formats actuels
**et** les anciens `.doc`, `.xls`, `.ppt` — l'OpenDocument, et les modèles des deux
familles. Un partage d'établissement en est plein : factures, échéanciers, formulaires.

Les anciens formats passent par LibreOffice **s'il est installé** sur la machine
d'indexation. LibreOffice est détecté, jamais embarqué : trois cent cinquante mégaoctets
et une surface d'attaque qui traite des fichiers venus de l'extérieur, ce n'est pas une
chose dont on veut devenir le mainteneur de correctifs auprès d'une école.

**Un administrateur peut verrouiller** l'un ou l'autre fournisseur pour que le réglage ne
soit plus modifiable sur le poste.

C'est ce qui rend le produit présentable à un collège, une université, une association ou
une entreprise qui traite des données sensibles : la recherche existe, et elle ne sort
pas de chez eux.

---

## Les décisions de conception

Trois choix expliquent la plupart des autres.

### Aucune injection, jamais

SteamXBox ne place jamais de code dans un autre processus. Pas de DLL dans
`explorer.exe`, pas de détour d'API système.

Ce n'est pas de la prudence excessive : un logiciel qui injecte se fait signaler par les
antivirus, casse à chaque mise à jour cumulative de Windows, et emporte le shell avec lui
quand il plante. Aucun service informatique d'établissement ne déploie ça.

Le coût est assumé et il est réel : certains effets sont hors d'atteinte et le resteront.
Sur ses propres fenêtres, en revanche, SteamXBox fait ce qu'il veut, puisque c'est lui qui
les dessine.

### L'hôte dessine, l'outil décrit

Un outil ne dessine pas sa fenêtre. Il déclare ce qu'il contient ; l'environnement fournit
la fenêtre, le thème, la navigation à la manette et le clavier virtuel.

C'est la contrainte la plus forte du contrat, et la plus importante. Dix outils écrits par
dix personnes qui dessinent chacune leur fenêtre donnent dix fenêtres étrangères, dont
aucune ne se pilote à la manette. En laissant l'hôte dessiner, **un outil écrit par un
utilisateur est navigable à la manette sans que son auteur y ait pensé**.

### Un outil est un dossier

Déposé, il est installé. Jeté, il est désinstallé. Rien à recompiler, rien à déclarer
ailleurs, aucune trace laissée derrière.

Le premier niveau ne contient **aucun code** : un fichier `plugin.json` et rien d'autre. Un
outil téléchargé quelque part ne peut donc rien faire de plus que ce que son manifeste
montre à qui le lit.

Les outils qui doivent calculer quelque chose passeront à un niveau supérieur, dont le
contrat sera écrit quand un vrai besoin le demandera — pas deviné à partir de zéro outil.

---

## Pour qui

**Le salon.** Un PC branché sur un téléviseur, une manette, et plus besoin de se lever
pour chercher un clavier.

**L'accessibilité.** Pour qui une souris est difficile, une manette est souvent plus
accessible — et ici tout l'environnement est conçu pour elle, pas seulement émulé.

**Les établissements et les organisations.** Collèges et lycées, universités, associations
à but non lucratif, entreprises traitant des données sensibles. Ce qui les concerne :
la recherche auto-hébergée, les verrous administrateur, l'absence d'injection, et le fait
que rien ne parte vers un service tiers.

---

## Comment c'est construit

- **.NET 10**, C#, WPF. Exécutables **autonomes** : aucun runtime à installer.
- Distribution **portable** (une archive à extraire) ou par installeur.
- **727 tests** automatisés. Les avertissements du compilateur sont traités en erreurs.
- Journalisation avec niveaux, catégories et rotation, et une commande de diagnostic.
- Interface en **français et anglais**, suivant la langue de Windows au premier lancement.

Un principe de travail traverse le projet : **ce qui touche à Windows est mesuré sur une
vraie machine, pas déduit**. Plusieurs comportements du système se sont révélés contraires
à ce que la documentation laissait attendre, et chaque fois c'est la mesure qui a tranché.

---

## Où en est le projet

Honnêtement : **pré-alpha** sur la partie extensible.

Ce qui fonctionne aujourd'hui : le pont manette, les manettes indépendantes et leurs
profils, les deux claviers à l'écran, l'environnement et son centre de contrôle, le
lanceur de recherche, l'indexeur documentaire, les fournisseurs web et documents.

Ce qui n'est pas écrit : **le chargeur de plugins**. Le contrat est fixé et documenté ; la
calculatrice et le presse-papiers sont compilés dans l'environnement et servent de
référence — le chargeur sera considéré comme juste le jour où il produira la même tuile
qu'eux à partir d'un `plugin.json` seul.

---

## Distribution

SteamXBox est un logiciel **propriétaire, tous droits réservés**, distribué en version
**gratuite et payante**. Les composants tiers conservent leurs licences respectives, qui
sont énumérées avec le produit.

Les dépendances système — ViGEmBus, obligatoire, et HidHide, optionnel — sont des pilotes
libres tiers que l'installeur complet peut poser pour l'utilisateur.
