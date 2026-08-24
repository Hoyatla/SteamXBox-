# Contrat MCP — ce qu'un modèle local a le droit de faire

SteamXBox est un **serveur** MCP : il expose ce que la machine sait déjà faire, et un modèle local
l'appelle. Rien ne part de la machine, aucun modèle n'est embarqué, et le serveur ne fonctionne que
tant qu'un client le lance.

Le contrat tient en trois portées, avec trois droits qui ne se mélangent pas.

---

## 1. Le produit — comprendre et commander

Core, Desktop, Moniteur, clavier à l'écran, et ceux qui suivront.

Le modèle peut lire l'état et déclencher des actions **en direct**. Une seule règle, et elle est
absolue :

> **Aucun verbe qui n'existe pas déjà pour l'utilisateur.**

Ouvrir un outil, basculer un mode, afficher le clavier, lire quelles manettes sont là : tout ça, une
personne le fait déjà à la manette. Le modèle obtient les mêmes verbes, jamais un de plus. S'il peut
faire une chose que personne ne peut faire à la manette, la porte est trop grande — et c'est ça,
dénaturer le produit.

Ce qui agit demande une confirmation **dessinée par l'environnement**, jamais par le serveur : la
fenêtre appartient à l'hôte, c'est déjà le contrat des outils. Et chaque appel est journalisé.

---

## 2. L'outil — comprendre, et rien d'autre

Le `plugin.json` d'un outil est **en lecture seule, définitivement.**

Le modèle le lit pour savoir à quoi sert l'outil, quelles entrées il attend, ce qu'il produit. Il ne
l'écrit pas, ne le complète pas, ne le corrige pas.

La raison n'est pas la prudence : un modèle qui réécrit la définition d'un outil pendant qu'on s'en
sert change l'outil sous les doigts de l'utilisateur. L'outil qu'on a ouvert doit être celui avec
lequel on finit.

> Créer un **nouvel** outil reste possible et sûr — un manifeste déclaratif, sans code, validé contre
> le schéma, montré avant d'être posé, désinstallé en jetant le dossier. Créer n'est pas modifier :
> un outil neuf ne trahit personne, un outil modifié en cours d'usage, si.

---

## 3. La session d'outil — l'univers d'édition

C'est là, et seulement là, que le modèle **écrit**.

Une session naît quand l'utilisateur ouvre un outil et meurt quand il le ferme. Elle porte une
identité, une portée de fichiers, et l'état de *cette* utilisation : le document ouvert, le texte en
cours, les réglages du moment.

Le modèle y lit ce sur quoi l'utilisateur travaille, comprend ce qu'il cherche à faire, propose, et
modifie ce qu'on lui demande de modifier. Un appel qui vise un fichier hors de la portée de la
session est refusé **par construction**, pas par vigilance.

C'est ce qui sépare un assistant utile d'un danger : il travaille avec l'utilisateur sur son
document, pas sur son installation.

---

## 4. Les briques, et l'éditeur d'outils

Le vocabulaire d'un manifeste — `file`, `choice`, `action`, et ceux qui suivront — **est** la boîte
de briques. Un outil existant n'est pas seulement un outil : c'est un exemple assemblé, et c'est ce
qui rend l'éditeur possible sans partir d'une page blanche.

D'où une règle pour le chargeur, à tenir dès sa première ligne :

> **Le vocabulaire est décrit une fois et lu trois fois.** Le chargeur le lit pour dessiner,
> l'éditeur pour proposer sa palette, le serveur MCP pour l'expliquer au modèle.

Codé en dur dans le chargeur, il faudrait le réécrire dans l'éditeur, puis une troisième fois pour le
modèle — et trois listes divergent toujours. Décrit une fois, une brique ajoutée apparaît d'elle-même
dans la palette et dans ce que le modèle sait faire.

### Ce que l'éditeur change au droit sur les outils

Rien, et c'est le signe que le contrat est juste.

Dans l'éditeur, un `plugin.json` n'est pas un outil en cours d'usage : c'est **le document de la
session d'édition**. Il relève donc de `session.*`, pas de `outil.*`. La règle tient sans exception :

- l'outil qu'on **utilise** ne se modifie pas sous les doigts de l'utilisateur ;
- l'outil qu'on **ouvre dans l'éditeur** est un document, et un document s'édite — par l'utilisateur,
  par le modèle, ou par les deux.

Le modèle a donc accès à la même palette que l'utilisateur, avec les mêmes briques et les mêmes
limites. Il n'a pas de vocabulaire privé.

---

## Les trois familles d'appels

| Famille | Droit | Portée |
|---|---|---|
| `produit.*` | lire, commander | les verbes que l'utilisateur a déjà |
| `outil.*` | lire | les manifestes de `Plugins/` |
| `session.*` | lire, écrire | le document et l'état d'une session ouverte |

Un appel hors de sa famille n'est pas une erreur d'usage : c'est un appel qui ne doit pas exister.

---

## Ce qui est fait, ce qui ne l'est pas

**Fait** — le serveur (`src/SteamXBox.Mcp/`), trois outils en lecture seule, transport stdio, aucune
dépendance. Le point d'écoute côté produit (`src/Sc2Xboxed.App.Console/McpBridge.cs`), un tuyau
nommé, une question par ligne. Posé et retiré par les deux installeurs.

**Pas fait** — la ligne qui démarre le pont dans `Program.cs`. Le client du tuyau côté serveur MCP.
Les verbes de commande et leur confirmation.

**Bloqué** — `session.*` n'a rien à quoi s'attacher tant que **le chargeur de plugins n'est pas
écrit**. C'est la partie annoncée comme pré-alpha dans la présentation, et elle devient le chemin
critique de tout ce document : un modèle qui édite dans un univers d'outil suppose des univers
d'outils.
