# Empreinte système de SteamXBox

**Tout ce que SteamXBox modifie en dehors de son propre processus, qui le restaure, et quand.**

Établi le 11 août 2026 par balayage exhaustif du code et vérification de l'état réel de la machine
de développement. Ce n'est pas une liste de mémoire : chaque ligne renvoie à un fichier, et la
colonne « constaté » dit ce qui a été mesuré ce jour-là.

## Pourquoi ce document existe

Un défaut a été signalé depuis le début du projet — « SteamXBox casse mon système, et ça continue
après l'avoir fermé » — et il a été traité chaque fois comme un incident isolé. Il ne l'est pas.
C'est une **propriété de conception** : SteamXBox écrit de l'état qui vit en dehors de lui, et
presque tous ses chemins de restauration supposent une sortie propre.

Une sortie propre est le cas rare. L'utilisateur ferme la fenêtre de console, tue le processus,
la machine s'éteint. Toute restauration qui n'est écrite qu'à la ligne de sortie est une
restauration qui n'aura pas lieu le jour où elle compte.

## Les règles

Deux, et elles ne se négocient pas.

### 1. Le clavier et la souris physiques passent avant SteamXBox

> **SteamXBox ne doit jamais couper le clavier ni la souris physiques au profit de quoi que ce soit.**
> La saisie doit rester possible au clavier, à la souris **et** au clavier à l'écran, ensemble.

Posée par l'utilisateur le 11 août 2026, et elle est plus qu'une préférence : c'est la condition pour
qu'un défaut reste réparable. Un produit qui prend le clavier prend aussi le seul moyen d'atteindre
l'écran qui le rendrait — et il n'existe pas de bouton « annuler » qu'on ne puisse pas atteindre.

**Pourquoi elle est nécessaire ici en particulier.** Un ensemble manette-clavier arrive comme **un
seul appareil composite**. Sur la machine de développement, `VID_37D7&PID_2501` expose :

| Interface | Ce que c'est |
|---|---|
| `&MI_00` | manette Xbox — **ce que SteamXBox masque** |
| `&MI_01&COL01` | **clavier** |
| `&MI_01&COL02` | **souris** |
| le parent | l'appareil composite qui porte les trois |

Masquer l'interface manette est juste. Masquer le parent aurait emporté le clavier et la souris avec
elle. Rien dans le code ne l'interdisait ; ce n'était simplement jamais arrivé.

**Ce qui applique la règle aujourd'hui :** avant tout masquage, `DeviceTree.IsOrCarriesKeyboardOrMouse`
descend l'arbre du périphérique et refuse tout appareil qui est — ou qui porte — un clavier ou une
souris. Un identifiant que Windows ne sait pas décrire est refusé aussi : le doute joue contre le
masquage. Chaque refus est journalisé avec sa raison.

**Ce qui la touche encore et reste à examiner :**

| Élément | État |
|---|---|
| Masquage HID | **garde en place**, vérifiée contre les appareils réels |
| Crochet clavier bas niveau | ne gèle plus la frappe depuis le 11 août, mais voit toujours toutes les touches |
| Clavier à l'écran | au privilège de l'utilisateur : ne peut plus taper dans une fenêtre élevée |
| Mode lézard du Steam Controller | désactive l'émulation clavier-souris **de la manette** — voulu, et la manette la reprend d'elle-même dès que SteamXBox se tait |

Toute nouveauté qui touche à un périphérique d'entrée se mesure contre cette règle **avant** d'être
écrite, pas après.

### 2. Rien ne doit dépendre d'une sortie propre

> **Rien ne doit dépendre d'une sortie propre.** Ce qui est posé en dehors du processus se répare au
> démarrage suivant, par réconciliation entre ce qui est noté et ce qui existe — pas par confiance
> dans le fait que la sortie a eu lieu.

La colonne « survit à un crash » de chaque tableau ci-dessous est la liste de ce qui reste à faire.

**Deux façons de la respecter, et la seconde vaut mieux.** Réparer au démarrage suivant, comme le
masquage des manettes : correct, mais il faut une comptabilité, elle peut diverger de la réalité, et
elle l'a fait. Ou bien **maintenir l'état par un battement**, comme le mode lézard : il cesse
d'exister dès qu'on cesse de le tenir, il n'y a rien à noter et rien à réconcilier. Quand le choix
existe, prendre le second.

---

## 1. Pilotes et périphériques

| État posé | Où | Qui restaure | Quand | Survit à un crash ? |
|---|---|---|---|---|
| **Masquage HID** (`--cloak-on`, `--dev-hide`) | pilote HidHide, à l'échelle du système | `Release`, `ReleaseOne` **et** `Reset` | au départ de la manette, à la sortie, et au démarrage suivant | **oui — réparé au démarrage** |
| **Liste blanche d'applications** (`--app-reg`) | pilote HidHide | *personne* | jamais | **oui — jamais retiré** |
| **Manette virtuelle Xbox 360** | pilote ViGEmBus | `PadSender.DisconnectAsync`, et Windows en fermant le descripteur | sortie propre, ou mort du processus | **non — mesuré** |
| **Mode lézard désactivé** (Steam Controller) | dans la manette | la manette elle-même, dès que le battement cesse | à la seconde qui suit | **non** |

**Constaté le 11 août, SteamXBox fermé :** le masquage global était **actif**, avec **5 appareils
masqués** alors que la note de SteamXBox n'en revendiquait que **2**. Une entrée **vide**
(`--app-reg ""`) figurait dans la liste blanche du pilote. Une manette Xbox 360 présente était
masquée, donc invisible pour Steam et pour les jeux. La cause : la note vivait à côté de
l'exécutable, donc chaque copie de SteamXBox avait la sienne, et celle de la copie désinstallée est
partie à la corbeille avec elle.

### Le mode lézard se répare tout seul, et c'est voulu

La manette **réarme son émulation clavier-souris d'elle-même** quand plus rien ne lui dit de se
taire. C'est la raison d'être du battement toutes les 800 ms : tant que SteamXBox parle, la manette
se tait ; dès qu'il meurt, elle revient. Aucun état à réparer, personne à qui le demander.

**Ce document a d'abord dit le contraire, et c'était faux à moitié.** `Disable()` change deux
choses — les correspondances de touches *et* les deux trackpads — et le battement n'en répétait
qu'une. Après un crash, les boutons revenaient et **les trackpads restaient morts** : assez
fonctionnel pour paraître normal, assez cassé pour être inutilisable. Corrigé le 11 août : le
battement envoie le `Disable` lui-même, pas une copie de ses commandes.

C'est le modèle à copier ailleurs. **Un état maintenu par un battement n'a pas besoin d'être
restauré** — il cesse d'exister quand on cesse de le tenir.

### La manette virtuelle se retire d'elle-même

`ViGEmClient` détient un descripteur vers le pilote de bus, et la manette vit tant que ce descripteur
vit. Windows le ferme à la mort du processus, quelle qu'en soit la cause.

**Mesuré le 11 août :** six manettes virtuelles présentes pendant la session, **zéro six secondes
après un kill brutal**, et toujours zéro huit secondes plus tard.

Deux tentatives antérieures n'avaient rien démontré et le document le disait : la session tournait en
mode **profil**, où SteamXBox pilote le pointeur et ne crée aucune manette virtuelle. Il faut
`--start-mode gamepad` pour que la question ait un sens — une mesure sur le mauvais mode répond à une
autre question.

C'est le second cas d'état qui n'a besoin d'aucune restauration, après le mode lézard : **sa durée de
vie est déjà celle du processus.**

---

## 2. Crochets et écoutes à portée système

| État posé | Portée | Qui retire | Quand | Survit à un crash ? |
|---|---|---|---|---|
| **Crochet clavier bas niveau** (`WH_KEYBOARD_LL`) | **toutes les frappes de la machine** | `DoubleTapHotkey.Stop`, et Windows à la mort du processus | sortie, ou fin du processus | non — mais **nuisible pendant** |
| **Écoute du presse-papiers** (`AddClipboardFormatListener`) | toutes les copies de la machine | `ClipboardService.Stop` | sortie | non — mais **nuisible pendant** |

Ces deux-là ne survivent pas au processus, et c'est précisément ce qui les a rendus difficiles à
attribuer : le symptôme disparaît en fermant SteamXBox, ce qui ressemble à une coïncidence.

**Défaut trouvé et corrigé le 11 août :** le crochet était installé depuis le fil d'interface. Un
crochet bas niveau est livré **sur le fil qui l'a installé**, et chaque frappe de chaque application
attend sa réponse. Le service presse-papiers lisait le presse-papiers **dans la pompe à messages du
même fil** ; copier un dossier dans l'explorateur bloquait ce fil sur l'explorateur, donc bloquait le
clavier de toute la machine. Mesuré : **33 secondes de clavier mort dans toutes les applications**,
jusqu'à la fermeture de SteamXBox.

Le crochet a désormais un fil dédié qui ne fait rien d'autre. Le presse-papiers ne lit plus rien dans
la pompe et ignore les fichiers copiés.

---

## 3. Registre (persistant par nature)

| Clé | Écrite par | Qui restaure | Quand | Survit à un crash ? |
|---|---|---|---|---|
| `HKCU\Control Panel\Cursors` | `CursorInstaller.Apply` | `CursorInstaller.Restore` | **action explicite de l'utilisateur** dans les paramètres | **oui — jamais automatique** |
| Valeurs de thème Windows | `WindowsStateBackup.CaptureOnce` puis écriture | `WindowsStateBackup.Restore` | idem | **oui** |
| `HKCU\...\CurrentVersion\Run` | `WindowsStartupService` | l'utilisateur, dans les paramètres | idem | **oui** |

Ces trois-là sont **volontairement** persistants : un thème de curseurs qui s'annulerait à la
fermeture ne servirait à rien. Ils sont listés parce qu'un inventaire honnête inclut ce qu'on laisse
exprès, et parce qu'un désinstalleur devra les rendre.

**Constaté :** `windows-state-backup.json` date du 3 août et n'a jamais été restauré. Si un thème a
été appliqué ce jour-là, l'état d'origine est toujours dans ce fichier et pas dans le registre.

---

## 4. Fenêtres qui n'appartiennent pas à SteamXBox

| Action | Cible | Qui restaure | Quand | Survit à un crash ? |
|---|---|---|---|---|
| `MinimizeAll` (geste `§§`) | toutes les fenêtres du bureau | `RestoreOnExit` **et** `RepairOnStart` | sortie propre, et démarrage suivant | **oui — réparé au démarrage** |

**Réparé le 11 août.** L'état ne vivait qu'en mémoire : une session qui mourait l'écran vidé laissait
l'utilisateur remonter ses fenêtres une par une sans savoir ce qui les avait couchées. Une marque est
désormais écrite dans l'état partagé, effacée dès que l'écran est rendu.

**Elle n'est suivie que si ces fenêtres peuvent encore exister.** Restaurer aveuglément serait pire
que ne rien faire : `UndoMinimizeALL` relève **tout** ce qui est réduit, donc une marque laissée
par un incident d'il y a trois jours ferait surgir, au démarrage, chaque fenêtre rangée
volontairement le matin même. Une fenêtre réduite ne survit pas à un redémarrage : la marque n'est
suivie que si elle est postérieure au dernier démarrage de la machine, et simplement effacée sinon.

Vérifié le 11 août avec une marque datée de trois jours : effacée, **aucune fenêtre relevée**.
| `SetForegroundWindow` / `BringWindowToTop` | la fenêtre choisie par l'utilisateur | sans objet | — | non |

**Vérifié :** SteamXBox ne pose `HWND_TOPMOST` que sur **ses propres** fenêtres (le clavier à
l'écran). Aucune fenêtre étrangère n'est forcée au premier plan. Le verrou d'ordre Z n'agit que sur
la fenêtre d'environnement.

L'hypothèse « SteamXBox pose des drapeaux topmost partout », soutenue puis abandonnée plusieurs fois,
est **fausse** et ce document la clôt.

---

## 5. Processus enfants

| Processus | Lancé par | Qui l'arrête | Survit à un crash du parent ? |
|---|---|---|---|
| `SteamXBox.Core.exe` | le GUI | `CoreProcessService`, **et le noyau** | **non** |
| `Sc2Xboxed*.Osk.exe` | le Core, à l'ouverture du clavier | le Core, **et le noyau** | **non** |
| `SteamXBox-Moniteur.exe` | l'utilisateur, depuis une tuile | fermeture de sa fenêtre | **oui — et c'est voulu** |

### Le noyau s'en charge, pas un chemin de sortie

Chaque enfant que SteamXBox lance comme sa propre machinerie rejoint un **job object** portant
`KILL_ON_JOB_CLOSE`. Windows ferme le descripteur du job quand le processus qui le tient s'arrête —
sortie propre, exception, force-kill, coupure de courant — et le noyau termine alors tout ce qui est
dedans.

**Mesuré le 11 août, avec sa référence :**

| | Enfant après un kill brutal du parent |
|---|---|
| sans adoption | **survit** — orphelin |
| avec adoption | **meurt avec lui** |

**Pourquoi pas une chasse aux orphelins au démarrage.** Il aurait fallu un registre de ce qui a été
lancé, un moyen de distinguer notre égaré d'une copie que l'utilisateur a lancée exprès, et une
décision quand les deux sont indiscernables — trois occasions de se tromper en tuant le processus de
quelqu'un. Le job object n'en a aucune.

C'est le troisième état de ce document qui n'a besoin d'aucune restauration, après le mode lézard et
la manette virtuelle : **sa durée de vie est celle du parent, par construction.**

### Ce qui n'entre pas dans le job

Un outil lancé depuis une tuile appartient à l'utilisateur. Fermer l'environnement n'est pas une
raison de le lui retirer, et le moniteur de diagnostic est justement celui qu'on veut voir survivre à
la session qu'il observe.

**Trouvé au passage :** `OskPrewarm`, que ce document citait comme lanceur du clavier à l'écran,
**n'a aucun appelant**. C'est du code mort ; le vrai lancement est dans le Core.

---

## 6. Fichiers hors du dossier applicatif

`%LOCALAPPDATA%\SteamXBox\` — 18 Mo, dont :

| Fichier | Rôle | Remarque |
|---|---|---|
| `mail-index.json` (18 Mo) | index du courrier | **contient le contenu de la correspondance de l'utilisateur** |
| `search-history.json` | historique de recherche | données personnelles |
| `controller-*.json`, `profiles/`, `xbox-profiles/` | profils | à conserver |
| `settings.json`, `osk-settings.json` | réglages | à conserver |
| `plugins-choices.json` | outils activés | à conserver |
| `windows-state-backup.json` | registre d'origine | daté du 3 août, jamais restauré |

`plugins-disabled.json` et `stop.requested` ont été supprimés le 11 août : orphelins l'un et l'autre.

`%USERPROFILE%\Pictures\SteamXBox\` — captures d'écran, voulues.

`hidhide-hidden.state` — désormais dans l'état partagé, plus à côté de l'exécutable.

### Le courrier et l'historique sont chiffrés au repos

**Corrigé le 11 août.** L'index du courrier gardait le texte lisible de chaque message — pour ne pas
rouvrir une boîte de trois cents mégaoctets à chaque frappe — donc **dix-sept mégaoctets de la
correspondance de quelqu'un, en clair**, lisibles par tout ce qui sait ouvrir un fichier. L'historique
de recherche, plus court et donc plus inoffensif d'apparence, nomme les mêmes personnes et les mêmes
sujets.

Les deux passent maintenant par la protection de données de Windows : la clé vient du compte Windows
connecté, il n'y a **rien à retenir pour l'utilisateur et rien à stocker pour ce produit**. Un mot de
passe sur un index de recherche serait demandé à chaque lancement et donc désactivé ; une clé rangée
à côté du fichier qu'elle protège n'est pas une clé.

**Mesuré sur l'index réel :** 2 156 messages, migration au premier chargement, 348 ms ; relecture en
258 ms ; ni `"Subject":` ni `"From":` lisibles dans le fichier.

**Ce que ça protège et ce que ça ne protège pas.** Le fichier copié sur une sauvegarde, une clé USB,
un dossier synchronisé, ou lu depuis le disque monté dans une autre machine : illisible. Un programme
qui tourne sous le même compte Windows : il n'a qu'à demander à Windows de le déchiffrer. Aucun
chiffrement local ne protège de ça, et prétendre le contraire serait pire que le texte clair, parce
que ce serait cru.

Un fichier indéchiffrable est traité comme absent, pas comme une erreur : c'est un cache d'une boîte
aux lettres qui existe toujours, donc le coût est une reconstruction, pas une perte.

---

## 7. Deux copies du produit sur la même machine

| Version | Emplacement | Date | Démarre avec Windows |
|---|---|---|---|
| **3.2.0** | `C:\Users\User\AppData\Local\Programs\SteamXBox\` | 1 août | **oui** (`HKCU\...\Run`) |
| **4.6.0** | `D:\...\SteamXBox-portable-win-x64\` | 10 août | non |

**C'est la découverte la plus importante de cet inventaire.**

La copie enregistrée pour démarrer avec Windows est une **3.2.0 du 1er août**, antérieure à toutes
les corrections faites depuis. Elle contient le crochet clavier sur le fil d'interface, la lecture
synchrone du presse-papiers, et tout ce qui a été réparé depuis.

Corriger la copie de développement sur `D:` ne change rien à ce que la machine lance au démarrage.
C'est exactement la forme d'un défaut qui « revient » après avoir été corrigé, et qui traverse tout
le projet sans jamais se laisser attribuer.

**Constaté :** elle ne tournait pas au démarrage du 11 août — un marqueur `stop.requested` du 5 août
la fait probablement sortir immédiatement. Le risque est donc dormant, pas actif. Il n'en est pas
moins réel : rien ne garantit qu'il le reste.

---

---

## 8. Privilèges — la frontière que le produit s'inflige

> **Corrigé le 11 août 2026.** Les cinq exécutables demandent désormais `asInvoker`, vérifié dans les
> binaires déployés. La section est conservée entière parce qu'elle explique une classe entière de
> symptômes, et parce que deux capacités restent à éprouver — voir la fin.

| Exécutable | Manifeste avant | Après |
|---|---|---|
| `SteamXBox.exe` (GUI) | `requireAdministrator` | `asInvoker` |
| `SteamXBox.Core.exe` | `requireAdministrator` | `asInvoker` |
| `Sc2Xboxed*.Osk.exe` (clavier à l'écran) | `requireAdministrator` | `asInvoker` |
| `SteamXBox.Desktop.exe` (environnement) | aucun manifeste | inchangé |

**C'est probablement la cause structurelle de la plus grande partie des symptômes signalés depuis le
début du projet.**

Windows applique l'**UIPI** (*User Interface Privilege Isolation*) : un processus de privilège
inférieur ne peut ni envoyer de message de fenêtre, ni réordonner, ni manipuler, ni accrocher la
fenêtre d'un processus de privilège supérieur. Les conséquences observées :

- Le clavier à l'écran est **élevé et topmost**. Aucune fenêtre d'application normale ne passera
  devant lui, et rien de non élevé ne peut le réordonner. C'est « le clic ne met plus les fenêtres
  en avant » et « aucune fenêtre ne passe devant l'ancienne ».
- Un Gestionnaire des tâches non élevé ne peut pas arrêter ces processus.
- **`SteamXBox.Desktop` ne peut pas piloter `Core` ni l'OSK.** La signalisation par fichiers
  (`DesktopSignal`, « OSK close signal written ») existe pour contourner cette frontière — un
  contournement d'un problème que le produit s'est créé lui-même.

**Ce qui a réellement besoin de l'élévation :** `HidHideCLI`. C'est tout. ViGEm et l'accès HID n'en
demandent pas. L'OSK l'exige probablement pour pouvoir taper dans des fenêtres élevées — ce que le
manifeste ferait proprement avec `uiAccess="true"`, au prix d'une signature et d'une installation
dans `Program Files`.

**Ce que la mesure a dit.** L'élévation était supposée nécessaire pour HidHide. Testé le 11 août
depuis un shell **non élevé** : `--cloak-off` a changé l'état du pilote, puis `--app-reg` a inscrit
une entrée et `--app-unreg` l'a retirée. **Lecture et écriture réussissent sans administrateur.** La
seule justification du produit était fausse.

**Ce que ça coûte.** Le clavier à l'écran ne peut plus taper dans une fenêtre élevée — Gestionnaire
des tâches, éditeur du registre, installeurs. C'est une perte réelle, acceptée : élevé *et* topmost,
cette fenêtre ne pouvait être remise derrière aucune autre par aucune application de l'utilisateur.
La vraie solution est `uiAccess="true"`, qui donne l'accès aux fenêtres élevées sans élever le
processus ; elle exige une signature numérique et une installation dans `Program Files`, à faire
avant la vente.

**Ce qui reste à éprouver**, faute de matériel branché au moment du changement :

| Capacité | État |
|---|---|
| HidHide, lecture et écriture | **mesuré sans élévation** |
| Démarrage de Core, GUI, OSK | **mesuré** — `elevated : False` dans son propre diagnostic |
| Création de la manette virtuelle ViGEm | **non éprouvé** — demande une manette branchée |
| Ouverture du Steam Controller en HID | **non éprouvé** — aucun appareil Valve connecté ce jour-là |

Si l'une des deux dernières échoue, la réponse n'est pas de tout ré-élever : c'est d'isoler cette
opération précise dans un utilitaire élevé appelé à la demande.

---

## Fait le 11 août 2026

- **Les deux copies.** La 3.2 installée est désinstallée, son entrée de démarrage retirée à la main —
  son propre désinstalleur l'avait laissée derrière lui.
- **L'élévation.** Les cinq exécutables demandent `asInvoker`. La justification supposée — HidHide —
  a été mesurée fausse : lecture et écriture du pilote réussissent sans administrateur.
- **La note du masquage** vit dans l'état partagé, se complète au lieu de s'écraser, et migre depuis
  l'ancien emplacement.
- **La sortie propre existe.** La croix, `stop` et Ctrl+C rendent tous les trois les manettes ; il
  n'existait aucun chemin propre avant.
- **Le départ d'une manette rend son appareil**, y compris pour les manettes XInput, qui ne
  terminaient jamais leur flux.
- **Liste et interrupteur** ne sont plus confondus : un appareil inscrit n'est masqué que si le
  masquage est allumé.
- **Clavier et souris protégés** — voir la règle 1.
- **Une fuite** : le guetteur d'arrivées construisait une source par manette toutes les trois
  secondes et jetait les surplus sans les libérer.
- **Les guetteurs empilés.** La boucle qui attend le retour d'une manette construisait un lecteur à
  chaque tour et n'en libérait aucun ; comme tout y tournait sur le jeton **de la session**, les
  anciens continuaient de balayer jusqu'à la fin. Deux siestes, trois guetteurs. C'était « la même
  manette arrive deux fois » : des arrivées à huit dixièmes de seconde d'une boucle qui attend trois
  secondes ne peuvent pas venir d'une seule boucle. Un lecteur a désormais sa propre vie, le libérer
  l'arrête vraiment, et deux tests le retiennent — vérifiés en neutralisant la correction : **6
  balayages avant la libération, 13 après**.

## Ce qui reste à faire, par ordre de gravité

1. **Signer les binaires**, puis seulement alors passer le clavier à l'écran en `uiAccess="true"`.
   Les deux vont ensemble et l'ordre n'est pas négociable — voir ci-dessous.
2. **Prévenir avant une désinstallation** que les curseurs de Windows ont été remplacés, et proposer
   de les rendre. Rien ne le fait aujourd'hui.

### `uiAccess` : mesuré, et impossible aujourd'hui

Le clavier à l'écran tourne au privilège de l'utilisateur depuis le 11 août, ce qui lui interdit de
taper dans une fenêtre élevée. La réponse propre est `uiAccess="true"` — l'accès aux fenêtres élevées
sans élever le processus — que Windows accorde à deux conditions **toutes les deux obligatoires** :
un binaire **signé** par un certificat de confiance, et installé dans un **emplacement sûr**
(`Program Files` ou `System32`).

**Essayé le 11 août :** avec `uiAccess="true"`, non signé, lancé depuis `D:`, Windows **refuse de
démarrer le processus**. Pas de dégradation, pas d'avertissement — le clavier à l'écran ne se lance
plus du tout.

Basculer ce drapeau avant d'avoir le certificat **casse la fonctionnalité au lieu de l'améliorer**.
La condition est inscrite dans `Sc2Xboxed.Osk.csproj`, à l'endroit où quelqu'un la lira au moment de
signer.

### Les curseurs de Windows sont remplacés, et personne ne prévient

**Constaté le 11 août :** 16 des 19 valeurs de `HKCU\Control Panel\Cursors` diffèrent de la
sauvegarde. Celle-ci les enregistre vides — les curseurs par défaut de Windows — et le registre
pointe aujourd'hui vers `glassmain.cur`, `glasshelp.cur` et le reste du thème appliqué le 3 août.

**Ce n'est pas un défaut : c'est la sauvegarde qui fait son travail.** Le thème a été appliqué
volontairement, et l'écran des paramètres sait le retirer.

Le défaut est ailleurs, et il est réel : **rien ne prévient qu'une désinstallation laisserait ces
curseurs en place.** `windows-state-backup.json` est alors le seul endroit au monde où l'état
d'origine existe encore, dans un dossier que la désinstallation emporte. C'est le seul élément de ce
document dont la restauration dépend encore de quelqu'un qui y pense.

### Connu et non résolu

- **Une entrée vide dans la liste blanche de HidHide.** La ligne de commande refuse
  `--app-unreg ""` — et sort avec le code 0, donc en silence. Signalée à chaque démarrage, retirable
  seulement depuis la fenêtre de HidHide.
- **Un échec de test intermittent**, un sur quinze passages le 11 août, jamais reproduit, jamais
  identifié.

## Comment maintenir ce document

Toute nouvelle chose qui touche à autre chose que le processus de SteamXBox s'ajoute ici **avant**
d'être écrite, avec sa ligne de restauration remplie. Une ligne dont la colonne « survit à un crash »
dit « non » est un défaut, pas une note.
