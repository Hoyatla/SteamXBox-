# Atelier — Spec (SenSÉ)

> **Statut** : v0.1.0, MVP. Spec de référence pour l'implémentation et les évolutions futures.

## Concept

Un **atelier de graphes de nœuds** intégré à SenSÉ. L'utilisateur clique la tuile, l'atelier s'ouvre. Deux espaces (`Codage`, `Multimédia`) avec plusieurs onglets de graphes par espace. Chaque graphe est un DAG de nœuds qu'on peut lier pour produire un résultat. Les nœuds invoquent les modèles locaux (Qwen 3.5 4B via wrapper HTTP) et les MCPs (`pc-agent`, `mcp-saisie`, `mcp-cdp`, ComfyUI quand présent).

L'Assistant peut aussi appeler les verbes de l'Atelier via MCP pour exécuter un graphe ou composer un résultat **sans que l'utilisateur dessine**.

## Architecture

**Processus unique** `SenSÉ.Atelier.exe` qui fait DEUX choses en même temps :

1. **UI WPF** (canvas, palette, onglets, inspecteur)
2. **Serveur HTTP loopback** sur le port **8770** (`127.0.0.1`)

Pourquoi un seul process : le canvas doit réagir en live quand un nœud s'exécute, et on évite le round-trip HTTP interne. L'Assistant passe par HTTP parce qu'il vit dans un autre process (SenSÉ.Desktop) et doit pouvoir orchestrer un graphe sans ouvrir la fenêtre.

L'Atelier est **autonome** vis-à-vis de SenSÉ : pas de `ProjectReference` vers les autres projets. Il communique avec le reste via HTTP (LLM via l'endpoint de SenSÉ.Desktop, MCPs existants via leurs ports loopback).

## Emplacement

```
Outils/Atelier/
  plugin.json
  SenSÉ.Atelier.exe
  Graphes/
    codage/
    multimedia/
  NoeudsCustom/

src/SenSÉ.Atelier/
  SenSÉ.Atelier.csproj
  Program.cs
  Mcp/
    ServeurHttp.cs
    Verbes.cs
  Modele/
    Espace.cs
    Graphe.cs
    Noeud.cs
    Port.cs
    Lien.cs
    TypePort.cs
    ResultatExecution.cs
  Bibliotheque/
    CatalogueNoeuds.cs
    DefinitionNoeud.cs
    Codage/   (25 nœuds)
    Multimedia/   (10 nœuds)
  Composite/   (4 workflows prêts)
  Custom/
    NoeudCustom.cs
    CatalogueCustom.cs
  Execution/
    Moteur.cs
    ContexteExecution.cs
    JournalExecution.cs
  Serialisation/
    Persistance.cs
  ModeleLocal/
    ClientModele.cs
  Mcp/   (clients HTTP sortants)
    ClientSaisie.cs
    ClientCdp.cs
    ClientDebugApi.cs
    ClientComfyui.cs
  UI/
    App.xaml
    FenetreAtelier.xaml
    OngletEspaces.xaml
    OngletGraphes.xaml
    Palette.xaml
    Canvas.xaml
    Inspecteur.xaml
    VueNoeud.xaml
    VueLien.xaml
    VuePort.xaml
  Themes/
    Default.xaml
```

## Verbes MCP (port 8770)

Format de retour : `{ok:true, data:...}` ou `{ok:false, error:"..."}`. POST sur `/atelier/<verbe>` avec body JSON.

| Verbe | Méthode | Body | Retour |
|---|---|---|---|
| `graphe/nouveau` | POST | `{espace, nom}` | `{graphe_id}` |
| `graphe/dupliquer` | POST | `{graphe_id}` | `{graphe_id}` |
| `graphe/supprimer` | POST | `{graphe_id}` | `{}` |
| `graphe/renommer` | POST | `{graphe_id, nom}` | `{}` |
| `graphe/lister` | GET | — | `{graphes:[{id, espace, nom, modifie_le}]}` |
| `graphe/charger` | POST | `{graphe_id}` | snapshot complet |
| `graphe/exporter` | POST | `{graphe_id, chemin}` | `{}` |
| `graphe/importer` | POST | `{chemin}` | `{graphe_id}` |
| `noeud/ajouter` | POST | `{graphe_id, type, x, y, params}` | `{noeud_id}` |
| `noeud/supprimer` | POST | `{noeud_id}` | `{}` |
| `noeud/deplacer` | POST | `{noeud_id, x, y}` | `{}` |
| `noeud/modifier` | POST | `{noeud_id, params}` | `{}` |
| `noeud/dupliquer` | POST | `{noeud_id}` | `{noeud_id}` |
| `lien/creer` | POST | `{graphe_id, port_source, port_cible}` | `{lien_id}` (refuse si types incompatibles) |
| `lien/supprimer` | POST | `{lien_id}` | `{}` |
| `catalogue/espaces` | GET | — | `{espaces:[{id, nom, nb_types}]}` |
| `catalogue/types` | GET | `{espace}` (query) | liste complète des types |
| `catalogue/type` | GET | `{type_id}` (query) | détail d''un type |
| `executer` | POST | `{graphe_id, entree?}` | `{execution_id}` (async) |
| `executer_noeud` | POST | `{noeud_id, entree?}` | `{execution_id}` (test isolé) |
| `execution/etat` | GET | `{execution_id}` (query) | `{statut, noeuds:[...], sorties_finales}` |
| `execution/annuler` | POST | `{execution_id}` | `{}` |
| `annuler` (undo) | POST | `{graphe_id}` | `{}` |
| `refaire` (redo) | POST | `{graphe_id}` | `{}` |
| `custom/creer` | POST | `{nom, espace, ports, params, code, langage}` | `{type_id}` |
| `custom/supprimer` | POST | `{type_id}` | `{}` |
| `custom/lister` | GET | — | `{custom:[...]}` |

## Nœuds : philosophie

- **Granularité** : un verbe = un nœud. Les modèles locaux raisonnent mieux sur des briques fines qu''on compose que sur des macros opaques. Donc `texte_vers_image` ET `image_redimensionner` ET `image_crop` séparément, pas un seul `image_manipulation`.
- **Pédagogique** :
  - Nom en français, court, sans jargon (`Texte vers Image` au lieu de `TextToImageNode`)
  - Description = 1 phrase + cas d''usage
  - Paramètres : noms simples (`Décris l''idée` au lieu de `prompt`)
  - Tooltip sur chaque champ
  - Valeurs par défaut qui marchent pour 90% des cas

## Nœuds Codage (cible 25)

### Primitives (9)
1. `texte` (param : contenu)
2. `nombre` (param : valeur, min, max)
3. `booleen` (param : valeur)
4. `fichier_lire` (entrée : chemin → sortie : contenu texte)
5. `fichier_ecrire` (entrée : contenu, chemin → sortie : ok)
6. `lister_fichiers` (entrée : dossier, motif → sortie : liste de chemins)
7. `concatener` (entrée : liste de textes, séparateur → sortie : texte)
8. `executer_python` (entrée : code → sortie : stdout, stderr)
9. `executer_commande` (entrée : commande, args → sortie : stdout)

### LLM (10)
10. `llm_generer_code` (entrée : description, langage → sortie : code)
11. `llm_completer_code` (entrée : code partiel → sortie : code complet)
12. `llm_expliquer_code` (entrée : code → sortie : explication)
13. `llm_reviser_code` (entrée : code → sortie : code révisé)
14. `llm_generer_tests` (entrée : code, langage → sortie : tests)
15. `llm_traduire_code` (entrée : code, langage_cible → sortie : code traduit)
16. `llm_documenter` (entrée : code → sortie : docstrings)
17. `llm_refactorer` (entrée : code, objectif → sortie : code refactoré)
18. `llm_generer_nom` (entrée : description → sortie : nom de variable)
19. `llm_repondre_question` (entrée : contexte, question → sortie : réponse)

### MCP bridges (6)
20. `mcp_saisie_screenshot` (entrée : titre fenêtre → sortie : chemin image)
21. `mcp_saisie_cliquer` (entrée : x, y, bouton → sortie : ok)
22. `mcp_saisie_taper` (entrée : texte → sortie : ok)
23. `mcp_cdp_naviguer` (entrée : url → sortie : ok)
24. `mcp_cdp_eval_js` (entrée : code js → sortie : résultat)
25. `mcp_cdp_screenshot` (sortie : chemin image)

## Nœuds Multimédia (cible 10)

26. `texte_vers_image` (entrée : prompt, modèle → sortie : chemin image via ComfyUI)
27. `texte_vers_video` (entrée : prompt, durée → sortie : chemin vidéo)
28. `image_vers_video` (entrée : image, prompt, durée → sortie : chemin vidéo)
29. `texte_vers_son` (entrée : texte, voix → sortie : chemin audio)
30. `audio_vers_texte` (entrée : audio → sortie : transcription)
31. `image_vers_texte` (entrée : image → sortie : description via Qwen VL)
32. `extraire_frames` (entrée : vidéo, fps → sortie : liste images)
33. `fusionner_videos` (entrée : liste vidéos → sortie : vidéo)
34. `decouper_video` (entrée : vidéo, début, fin → sortie : vidéo)
35. `redimensionner_image` (entrée : image, largeur, hauteur → sortie : image)

## Nœuds composites (4 prêts à l''emploi)

36. `workflow_code_complet` (compose : llm_generer_code + llm_reviser_code + fichier_ecrire)
37. `workflow_tests_unitaires` (compose : llm_generer_tests + fichier_ecrire + executer_python)
38. `workflow_refactor_securise` (compose : llm_refactorer + llm_reviser_code + fichier_ecrire)
39. `workflow_texte_vers_animation` (compose : texte_vers_image + image_vers_video + fichier_ecrire)

## Nœuds custom

L''utilisateur peut créer ses propres nœuds. Un nœud custom = JSON dans `Outils/Atelier/NoeudsCustom/<type_id>.json` :

```json
{
  "id": "mon_outil",
  "nom": "Mon outil",
  "espace": "codage",
  "description": "Fait un truc utile",
  "ports_entree": [{"nom": "entree1", "type": "texte"}],
  "ports_sortie": [{"nom": "sortie1", "type": "texte"}],
  "params": [{"nom": "seuil", "type": "nombre", "defaut": 0.5}],
  "langage": "python",
  "code": "import sys\nresultat = entree1.upper()\nsortie1 = resultat"
}
```

**Exécution** : le code est sauvegardé dans un `.py` temp, exécuté avec les entrées comme variables, les sorties sont récupérées via le namespace du script.

## UI WPF

```
+--------------------------------------------------------------+
| [Codage] [Multimédia]                                   _ [] X |
+--------------------------------------------------------------+
| [Graphe 1*] [Graphe 2] [+]                  [Exécuter F5]    |
+----------+-----------------------------+---------------------+
|          |                             |                     |
| Palette  |          Canvas             |     Inspecteur      |
| (gauche) |          (centre)           |      (droite)       |
|          |                             |                     |
| ▼ Texte  |  [Texte] -> [llm_Rust]      | Noeud: llm_gener..  |
|  Texte   |                 |            |                     |
|  Nombre  |                 v            | Langage: [Rust v]   |
|          |         [Sauvegarder]        | Idée: [________]    |
| ▼ Code   |                             |                     |
|  ...     |                             | Description:        |
|          |                             | Génère du code...   |
| ▼ MCPs   |                             |                     |
|  ...     |                             |                     |
+----------+-----------------------------+---------------------+
```

- Drag & drop palette → canvas
- Drag port sortie → port entrée pour créer un lien (Bézier)
- Sélectionner un nœud → inspecteur à droite
- Bouton Exécuter ou F5

## Modèle local

L''Atelier ne lance PAS le modèle (double chargement évité). Il parle au modèle via HTTP.

Si l''API HTTP n''existe pas dans SenSÉ.Desktop, l''Atelier a un **fallback** : un mini-client configurable via `ATELIER_LLM_URL` (par défaut `http://127.0.0.1:8765/v1/llm/complete`). Si rien n''est accessible, les nœuds LLM renvoient une erreur explicite.

## Plugin manifest

`Outils/Atelier/plugin.json` :

```json
{
  "id": "atelier",
  "name": "Atelier",
  "category": "tool",
  "version": "0.1.0",
  "author": "Hoyatla",
  "licence": "MIT",
  "source": "https://github.com/Hoyatla/SenS-",
  "glyph": "E8A5",
  "hint": "Compose des graphes de nœuds : code, image, vidéo, son.",
  "surface": "tile",
  "does": "application",
  "target": "{tools}\\Atelier\\SenSÉ.Atelier.exe",
  "enabled": true
}
```

## Persistance

- Graphes : `Outils/Atelier/Graphes/<espace>/<graphe_id>.json`
- Noeuds custom : `Outils/Atelier/NoeudsCustom/<type_id>.json`
- Format JSON, indenté, UTF-8 sans BOM
- Sauvegarde auto à chaque modification (debounce 500 ms)
- Undo/redo : pile en mémoire

## Contraintes d''implémentation

- **net8.0-windows**
- **UseWPF=true**, **OutputType=WinExe**
- **SelfContained=true**, **PublishSingleFile=true**
- **Pas de ProjectReference** vers les autres projets SenSÉ (autonome)
- 0 erreur, 0 warning au build

## Roadmap post-MVP

- v0.2 : undo/redo persistant (fichiers `.bak`)
- v0.3 : subgraphs (nœuds qui contiennent d''autres graphes)
- v0.4 : streaming output pour LLM (affichage progressif dans le nœud)
- v0.5 : drag-resize des nœuds + snapping grille