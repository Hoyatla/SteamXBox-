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
