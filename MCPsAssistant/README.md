# MCPsAssistant

Les serveurs MCP (Model Context Protocol) que l'Assistant utilise pour
piloter le systeme, les outils et les programmes installes.

## Regle qui prime sur toutes les autres

Aucun serveur MCP n'injecte quoi que ce soit dans un autre processus.
Pas de DLL, pas de hook global, pas de detour d'API. Le pilotage se fait
par les voies documentees : API officielles (UNO, HTTP, COM), ou par les
verbes de saisie (souris, clavier, screenshot) en dernier recours.

## Ce qu'un serveur peut faire

| Serveur           | Portee                                                   |
|-------------------|----------------------------------------------------------|
| `mcp-saisie`      | Capture ecran, souris, clavier, screenshot de fenetre   |
| `mcp-documents`   | LibreOffice (UNO), PDF, Office, OCR                      |
| `mcp-generation`  | ComfyUI (HTTP), Flux, agrandisseurs                      |
| `mcp-fichiers`    | Explorateur, recherche, mails, mbox                      |
| `mcp-systeme`     | Parametres Windows, processus, scheduler                 |
| `mcp-assistant`   | Conversation, carnets, modeles locaux                    |

## Ce qu'un serveur ne peut PAS faire

Liste noire stricte, jamais, meme sur demande :
- Eteindre, redemarrer, mettre en veille le PC
- Deconnecter un peripherique
- Supprimer un fichier (meme dans `Outils\`)
- Envoyer un mail sans demande explicite
- Modifier un parametre Windows systeme (registre, services, taches planifiees)

Ces verbes sont absents du vocabulaire. Le modele ne peut pas les nommer
parce qu'ils n'existent pas.

## Gardien visuel : Mode Exclusif

Quand l'Assistant pilote la souris ou le clavier, un overlay WPF topmost
s'affiche en haut de l'ecran avec le bandeau :

    +----------------------------------------+
    |  L'ASSISTANT PILOTE                    |
    |  mcp-saisie / sequence en cours        |
    |  Appuie sur Echap pour interrompre     |
    +----------------------------------------+

L'utilisateur peut interrompre a tout moment avec Echap.

## Transport

Stdio JSON-RPC 2.0 pour la communication in-process. Pas de port, pas
de pare-feu, pas de conflit. HTTP est reserve aux clients externes.

## Cycle de vie

Un serveur MCP est lance quand l'Assistant est ouvert (tuile cliquee) et
arrete quand l'Assistant est ferme. Pas de processus en arriere-plan.

## Observateurs et proactivite

L'Assistant peut etre declenche sans demande utilisateur par des
observateurs qui poussent des evenements sur l'EventBus :
- FileSystemWatcher (fichiers dans Outils\Projets\)
- MboxWatcher (nouveaux mails)
- ProcessWatcher (crashes, deconnexions)
- TimerScheduler (taches planifiees)

Quand l'Assistant agit de sa propre initiative, il prefixe son
message dans la conversation par :

    [L'Assistant a fait X de lui-meme]

Pas de toast Windows, pas de notification systeme. Juste la
conversation, qui est deja ouverte puisque l'Assistant n'existe
que si l'utilisateur a clique la tuile.