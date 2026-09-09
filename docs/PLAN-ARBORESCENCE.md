# Plan d'exécution — arborescence et approvisionnement

*Écrit le 10 septembre 2026. À exécuter par quelqu'un d'autre que son auteur.*

Ce plan met en œuvre [arborescence.md](arborescence.md) et [installation.md](installation.md).
**Lis-les d'abord** : ils portent le *pourquoi*, celui-ci porte le *quoi* et le *comment vérifier*.

## Comment se servir de ce document

- Les lots sont **ordonnés** et **livrables séparément**. Un lot fini se commite seul.
- Chaque case se coche **quand la vérification passe**, pas quand le code est écrit.
- Le **journal de bord** en fin de document se remplit à chaque lot : plusieurs personnes peuvent se relayer.
- Un lot qui se révèle plus gros qu'annoncé : on le note au journal, on ne l'élargit pas en silence.

**Statut global : 0 / 13 lots.**

---

## Avant de commencer — ce qui fera perdre une soirée

Ces pièges sont mesurés, pas supposés. Ils ont tous coûté du temps à quelqu'un.

- [ ] **Lu et compris avant le premier lot.**

| piège | ce qu'il faut savoir |
|---|---|
| **Chemin accentué** | Les binaires natifs refusent l'accent de `SenSÉ` dans leurs **arguments**, jamais dans leur **répertoire courant**. Solution : `WorkingDirectory` + chemins relatifs. Ne jamais créer de jonction hors du produit. |
| **Publication** | `dotnet publish -o <racine du produit>` **casse la compilation** (CS5001). Publier dans `publish/<Projet>/`, puis copier. |
| **`PublishTrimmed`** | Interdit. Rend l'exécutable impossible à copier ou lancer sur cette machine. |
| **Mono-fichier qui échoue** | Ce n'est pas un bug : c'est un verrou transitoire de l'antivirus sur `obj\...\singlefilehost.exe`. `dotnet build-server shutdown`, puis republier. **Ne pas basculer en multi-fichiers** — ce serait 150 DLL à la racine. |
| **`Outils/*` est gitignoré** | Les manifestes et documents qu'on veut versionner s'ajoutent avec `git add -f`. Jamais les binaires ni les poids. |
| **PowerShell 5.1** | Lit un `.ps1` sans BOM en ANSI : un accent casse le script. Écrire avec BOM. |
| **Travail parallèle** | L'arbre de travail a été réinitialisé **trois fois** le 9 septembre par un autre chantier. **Commiter tôt et souvent.** |
| **Ne jamais `push`** | Commits seulement. C'est une consigne du propriétaire du dépôt. |
| **Rien hors du dossier source** | Aucun fichier, dossier ou jonction en dehors de `C:\Program Files\SenSÉ`. |

**État de départ de la suite de tests : 1 494 vertes, 1 rouge connue** — `PluginsLivresTests.TheFlowToolMatchesTheFlowItShips`, périmée depuis le retrait du plugin `flux-image-video`. Toute autre rouge est de vous.

---

# Partie I — L'arborescence

## Lot 0 — L'updater vise le bon produit

**Pourquoi d'abord :** tout l'installeur s'appuiera sur cet identifiant. Bâtir dessus avant de le corriger, c'est le corriger deux fois.

Mesuré dans `updater/updates.json` : `productName: "SteamXBox"`, clé `SOFTWARE\Hoyatla\SteamXBox`, `helpUrl` vers `SteamXBox-Explorer`. Le renommage v0.6.0 ne l'a pas suivi.

- [ ] `updater/updates.json` et `updater/Hoyatla_SenSÉ_Updater.json` : `productName`, `windowTitle`, clé de registre, `helpUrl`.
- [ ] Chercher les autres survivances : `grep -ri "SteamXBox" --include=*.json --include=*.iss --include=*.ps1`
- [ ] **Décider et noter** au journal : la clé de registre change-t-elle de nom, ou garde-t-elle l'ancienne pour que les installations existantes soient reconnues ? Les deux se défendent ; le silence non.
- [ ] **Vérification** : une installation de test s'enregistre et se détecte sous le bon nom.

---

## Lot 1 — `Chemins`, le résolveur

**Pourquoi d'abord :** sans lui, tous les lots suivants dérivent. Ce n'est pas une précaution théorique — voir §6 de [arborescence.md](arborescence.md) : la convention `Debug/` existait, documentée, et personne ne l'appliquait à l'écriture. Trois pannes muettes en ont découlé.

Modèle à suivre : `src/SenSÉ.Core/Diagnostics/CheminDebug.cs`, qui a corrigé exactement ce défaut.

- [ ] `src/SenSÉ.Core/Chemins.cs` — `Racine`, `Moteurs`, `Programmes`, `Creations(genre)`, `Recu(sujet)`, `Carnets`, `Memoire`, `Travail`, `AssurerRacine()`, `Purger(TimeSpan)`.
- [ ] Une propriété `Racine { get; set; }` **de test**, comme `CheminDebug` et `Moteurs` en ont une : la suite ne doit jamais écrire dans le produit.
- [ ] Le nommage daté en un seul endroit : `aaaammjj-hhmm-<limace>`. Déjà implémenté deux fois — `RechercheWeb.Limace` et `Redaction.Limace`. **Les faire converger ici.**
- [ ] **L'épreuve qui tient tout** : aucun `Path.Combine(AppContext.BaseDirectory, …)` visant ces dossiers hors de `Chemins`. Un test qui balaie les sources et échoue sur le motif. *Une note dans un document existait déjà et n'a rien empêché.*
- [ ] **Vérification** : le test de motif passe, et il échoue si on réintroduit volontairement un chemin recomposé.

---

## Lot 2 — Sortir le travail de la zone jetable

**C'est le seul lot qui protège quelque chose d'irremplaçable. Il ne devrait pas attendre les autres.**

`Outils/Atelier/Graphes/` et `Outils/Atelier/NoeudsCustom/` sont du travail fait à la main, posé au milieu de 80 Go dont tout le reste se retélécharge. Un « je vide `Outils/` » raisonnable le détruit.

- [ ] Créer `Mes créations/Graphes/`, `Mes créations/Noeuds/`, `Mes créations/Gabarits/`.
- [ ] **Déplacer** le contenu existant. Ne pas copier : deux exemplaires divergent.
- [ ] Rediriger les lecteurs et écrivains, tous par `Chemins` : `src/SenSÉ.Atelier/` — `Persistance`, `Templates`, `CatalogueNoeuds` (nœuds personnalisés), `Mcp/Verbes.cs` (`graphe/*`, `custom/*`, `templates/*`).
- [ ] **Migration à l'ouverture** : si l'ancien emplacement existe encore, déplacer et journaliser. Un utilisateur qui met à jour ne doit rien faire à la main.
- [ ] **Vérification** : un graphe créé avant la migration se recharge après ; `Outils/Atelier/Graphes` n'existe plus.

---

## Lot 3 — `Reçu/`

Le manque à l'origine de la demande : rien, aujourd'hui, ne range ce qui est téléchargé.

- [ ] `Chemins.Recu(sujet)` crée `Reçu/aaaammjj-hhmm-<sujet>/`.
- [ ] `PROVENANCE.json` : `url`, `recu_le`, `demande_par`, `sujet`, `type`, `octets`, `sha256`. Format en §5 de [arborescence.md](arborescence.md).
- [ ] **Rien ne s'exécute depuis `Reçu/`** — à faire tenir par un test, pas par une convention.
- [ ] Une capacité `telecharger` pour l'assistant, qui écrit là et **nomme le fichier produit** (pour que `FichierProduit` puisse enchaîner).
- [ ] **Vérification** : un téléchargement laisse dossier daté + provenance complète ; deux téléchargements du même nom font deux dossiers.

---

## Lot 4 — Regrouper les productions

- [ ] `Mes créations/Images|Videos|Audio|Documents|Recherches/`.
- [ ] Déplacer `Creations/` (38 fichiers, 26 Mo) en triant par type.
- [ ] Rediriger `RechercheWeb.Dossier` (aujourd'hui `Travaux/Recherches/`) et `Redaction.Ecrire` (aujourd'hui `Travaux/Documents/`) vers `Chemins`.
- [ ] Rediriger `Captures/` (mcp-cdp, mcp-saisie) vers `Mes créations/Images/`.
- [ ] `Travaux/` → `Carnets/` : `FichierTravail.Dossier`.
- [ ] **Vérification** : une recherche, un document et une capture atterrissent aux trois bons endroits.

---

## Lot 5 — `Outils/` → `Moteurs/` + `Programmes/`

Le plus gros diff, le moins urgent. **À ne pas faire à moitié** — c'est exactement ce qui a produit les pannes de `Debug/`.

- [ ] `Outils/Modeles/` → `Moteurs/` : `Moteurs.Dossier`, les sept `modele.json` (chemins relatifs, à revérifier), `ServeurModele`.
- [ ] `Outils/` → `Programmes/`, avec `Programmes/SenSÉ/` pour les binaires du produit (Atelier, ÉditeurTexte, Cdp, McpSaisie, DebugAgent).
- [ ] Recenser **avant** : `grep -rn 'BaseDirectory, "Outils"' src/` — une quarantaine d'appels. Tous doivent passer par `Chemins`.
- [ ] Manifestes de plugins : le jeton `{tools}` désigne `Outils`. Décider s'il pointe vers `Programmes` ou s'il disparaît. **Noter la décision.**
- [ ] `SenSÉ_Installer.iss` et `SenSÉ_Full_Installer.iss`.
- [ ] **Vérification** : après renommage, l'Assistant démarre un moteur, l'Atelier répond sur 8770, mcp-cdp navigue, un langage s'exécute.

---

## Lot 6 — Purge par âge

- [ ] `Chemins.Purger` sur `Reçu/` et `Travail/`, en réemployant la logique de `CheminDebug.Purger` (30 jours, par âge, jamais par balayage).
- [ ] **Par âge et jamais « tout vider »** : la raison est écrite dans `CheminDebug` — un `.signal` frais est un ordre en vol. La même prudence vaut pour un téléchargement en cours.
- [ ] **Vérification** : un fichier de 45 jours part, un fichier frais reste.

---

## Lot 7 — Le ménage final

- [ ] Supprimer `Flux/` — workflows ComfyUI, qui n'existe plus.
- [ ] `Modeles/jeux-modeles.json` → `Programmes/SenSÉ/`, et supprimer le dossier `Modeles/` de la racine (il crée une ambiguïté avec `Moteurs/`).
- [ ] Retirer la couche morte du générateur : `SenSÉ.Tools/Generation/ComfyServer`, `FluxTravail`, `SequenceAnimee`, `Catalogue`, `OptionsVivantes`, `PaquetGenerateur` — **après avoir vérifié** qu'aucun plugin ne déclare d'action `flux`, `generateur` ou `sequence` (c'était vrai le 9 septembre).
- [ ] Retirer le test périmé `PluginsLivresTests.TheFlowToolMatchesTheFlowItShips`, **en écrivant dans le commit pourquoi il n'a plus d'objet**.
- [ ] **Vérification** : suite entièrement verte.

---

# Partie II — L'approvisionnement

## Lot 8 — Le catalogue

- [ ] `catalogue.json` versionné avec le produit. Format en §5 de [installation.md](installation.md).
- [ ] Le remplir **en mesurant la machine actuelle** : chaque fichier de `Outils/Modeles` et `Outils/*`, sa taille réelle, son SHA-256 calculé, sa source.
- [ ] `empreinte_publiee: false` quand la source n'en publie pas (instantanés Chromium). **Le dire, ne pas le taire.**
- [ ] Reprendre les cinq plugins `dependance-*` : ils ont déjà `licence`, `target`, `source`. Il leur manque empreinte, taille, version épinglée — et deux `source` sont des **pages**, pas des fichiers (LibreOffice, Poppler).
- [ ] Marquer Poppler **non livrable** (GPL) : à proposer, jamais à installer.
- [ ] **Vérification** : un test relit le catalogue, vérifie que chaque entrée est complète ou explicitement marquée incomplète.

---

## Lot 9 — `Approvisionnement`

Généralise `src/SenSÉ.Mcp.Bus/ChromiumEmbarque.cs`, qui fait déjà bien la moitié du travail.

- [ ] Reprendre tel quel : écriture `.part`, renommage atomique, version épinglée, HTTPS officiel, vérification que l'archive s'ouvre.
- [ ] Ajouter : **vérification SHA-256 avant usage**, reprise par plages HTTP (`Range`), état persistant sur disque, parallélisme borné à 2 ou 3.
- [ ] Écrire la provenance de chaque acquisition, au format de `Reçu/`.
- [ ] **Vérification** : couper le réseau au milieu d'un téléchargement, relancer — il reprend et ne recommence pas. Corrompre un `.part` — il est refusé, pas installé.

---

## Lot 10 — Les paliers

- [ ] Déclarer les huit paliers du §2 de [installation.md](installation.md).
- [ ] Défaut : **Socle + Assistant + Voix + Web = 11,3 Go**. Pas 80.
- [ ] L'installeur **rend la main dès le palier Assistant** ; le reste continue en tâche de fond.
- [ ] Un palier se retire aussi bien qu'il s'ajoute.
- [ ] **Vérification** : une installation par défaut donne un assistant qui dialogue, transcrit et cherche sur le web.

---

## Lot 11 — La connexion

- [ ] Vérifier **l'hôte dont on dépend**, pas un site témoin.
- [ ] Honorer le proxy système et les fichiers PAC.
- [ ] **Nommer le portail captif** : du HTML là où on attend un binaire dit « un portail intercepte la connexion », jamais « fichier corrompu ». Modèle : la détection de contrôle anti-robot dans `RechercheWeb`.
- [ ] **Vérification** : derrière un proxy, ça marche ; derrière un portail captif, le message est juste.

---

## Lot 12 — L'archive compagnon

- [ ] L'installeur accepte un dossier ou une archive locale à la place du réseau, **même catalogue, source différente**.
- [ ] Empreintes vérifiées de la même façon — une clé USB n'est pas plus digne de confiance qu'un serveur.
- [ ] **Vérification** : installation complète sur une machine sans réseau.

---

## Lot 13 — La provenance partout

- [ ] Chaque chose téléchargée — modèle, programme, runtime — laisse d'où elle vient, quand, et avec quelle empreinte.
- [ ] Un état lisible : ce qui est installé, en quelle version, d'où.
- [ ] **Vérification** : « d'où sort ce fichier de 9 Go » a une réponse sans deviner.

---

## Journal de bord

À remplir en avançant. Une ligne par lot terminé.

| lot | fait le | par | commit | notes, décisions, surprises |
|---|---|---|---|---|
| | | | | |

### Décisions à noter ici quand elles seront prises

- Lot 0 : la clé de registre change-t-elle de nom ?
- Lot 5 : `{tools}` pointe vers `Programmes`, ou disparaît ?
- Lot 5 : `Moteurs/` reste-t-il sur le disque système, ou devient-il déplaçable ? (76 Go sur un SSD système, c'est une question réelle.)

---

*Rien dans ce plan ne sort de `C:\Program Files\SenSÉ`.*
