# Mise à jour automatique de SteamXBox

L'agent est [**Vīcĭus**](https://github.com/nefarius/vicius), de Nefarius Software Solutions —
l'auteur de HidHide et de ViGEmBus, dont SteamXBox dépend déjà. Licence BSD-3-Clause.

Il est pris tel quel parce que **SteamXBox n'avait aucune mise à jour automatique** : ce n'est pas
le remplacement d'un travail existant, c'est une fonction qui manquait.

## Comment il fonctionne

Il ne réside pas. Il s'exécute à l'ouverture de session et **une fois par jour** par le planificateur
de tâches, compare la version installée à celle d'un manifeste publié, et ne montre une fenêtre que
s'il y a quelque chose à proposer.

**Le serveur est un fichier JSON statique.** Aucune application serveur, aucune base : GitHub Pages
suffit.

## Les deux fichiers d'ici

| Fichier | Où il va | Rôle |
|---|---|---|
| `Hoyatla_SteamXBox_Updater.json` | à côté de l'agent, dans `Program Files` | dit à l'agent où chercher et comment détecter la version installée |
| `updates.json` | **publié en ligne** | ce que l'agent va lire : la dernière version, son lien, ses notes |

Le nom du fichier de configuration doit être **exactement** celui de l'exécutable avec `.json` —
c'est ainsi que l'agent le trouve. Et le nom de l'exécutable encode le fabricant et le produit :
`Hoyatla_SteamXBox_Updater.exe` construit l'adresse `…/api/Hoyatla/SteamXBox/updates.json`.

## Comment la version installée est détectée

Par le registre : `HKLM\SOFTWARE\Hoyatla\SteamXBox` → `Version`, écrite par l'installeur et emportée
par la désinstallation. Sous `HKLM` et non `HKCU` parce que la tâche planifiée ne sait pas quel
utilisateur a installé le produit.

**Sans cette valeur, l'agent ne proposera jamais rien** — c'est son seul moyen de savoir ce qui tourne.

## Il attend que SteamXBox soit libre

`productBusyDetection` liste les exécutables du produit. Tant que l'un d'eux tourne, l'agent ne
propose rien : mettre à jour pendant que SteamXBox tient les manettes remplacerait les binaires sous
un pilote actif, avec un masquage HidHide en cours et des manettes virtuelles branchées.

Deux heures d'attente au maximum, puis il renonce jusqu'au lendemain.

## Ce qu'il reste à faire pour que ça vive

1. **Télécharger l'agent** depuis [les publications de vicius](https://github.com/nefarius/vicius/releases)
   et le renommer `Hoyatla_SteamXBox_Updater.exe` dans ce dossier. *Non fait : télécharger un
   exécutable est une décision qui appartient à l'éditeur du produit, pas à son outillage.*

2. **Publier `updates.json`.** L'adresse suppose GitHub Pages sur le dépôt du projet,
   `SteamXBox-Explorer` :

   ```
   https://hoyatla.github.io/SteamXBox-Explorer/api/Hoyatla/SteamXBox/updates.json
   ```

   Le segment `Hoyatla/SteamXBox` de la fin n'est **pas** un dépôt : c'est le fabricant et le
   produit, tels que le nom de l'agent les encode (voir plus haut). Il ne suit pas le nom du dépôt
   et ne doit pas être renommé avec lui.

   Reste à activer Pages sur `SteamXBox-Explorer` et à y déposer le fichier ; tant que ce n'est pas
   fait, l'agent ne trouve rien et ne propose donc aucune mise à jour.

3. **Écrire la première publication** dans `updates.json`. La liste `releases` est **vide**, et
   c'est volontaire : rien n'a encore été publié dans la suite qui commence à la 0.5.0.

   Elle portait les 4.8.2 et 4.7.0 de l'ancienne numérotation. Les laisser aurait retourné
   l'agent contre le produit : il compare des numéros, il aurait vu une 4.8.2 « plus récente »
   qu'une 0.5.0 installée, et proposé au client d'installer par-dessus une version antérieure —
   dont les fichiers vivent d'ailleurs sur l'autre dépôt. Leurs notes ne sont pas perdues : le
   `ChangeLog.txt` du produit les garde toutes.

   `downloadSize` se renseigne à ce moment-là : la taille exacte en octets du programme
   d'installation publié, et non `0`, qui n'est un marqueur que tant que rien n'est publié.

4. **Signer l'agent et l'installeur.** `signatureVerificationMode` est sur `WhenPresent` : la
   signature est vérifiée si elle existe. Une fois les binaires signés, passer à `Required` fait de
   la vérification une condition — c'est ce qui empêche qu'un manifeste détourné fasse installer
   autre chose.

## À chaque publication

Mettre à jour, dans `updates.json` : `version`, `publishedAt`, `downloadUrl`, `downloadSize` et
`summary` (du Markdown, affiché tel quel dans la fenêtre). Puis republier le fichier. C'est tout —
il n'y a rien à redéployer côté client.
