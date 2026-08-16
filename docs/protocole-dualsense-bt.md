# Relever la correspondance des commandes d'une manette

`Banc-DualSense.exe`, à la racine. Lecture seule : rien n'est écrit sur la manette.

## Avant

1. `Stop-SteamXBox.cmd` — un périphérique HID a un propriétaire à la fois.
2. Fermer Steam.
3. `HidHide-Off.cmd` — HidHide masque les manettes à tout processus sauf le Core, et sans ça le banc
   ne voit rien.

## Pendant

Double-clic. Une liste de commandes s'affiche, la première surlignée. On presse celle qui est
surlignée, le banc voit ce qui a bougé dans le rapport, l'inscrit en face et passe à la suivante.

- **Flèche droite** : passer la commande surlignée.
- **Échap** : terminer et écrire le rapport.

Une commande qui ne produit rien est inscrite « rien détecté », et c'est une mesure : elle n'existe
pas dans cette forme de rapport. C'est comme ça qu'on apprend que le pavé tactile ne donne aucune
coordonnée en Bluetooth, ou que les gâchettes n'y sont pas analogiques.

Le repos est relevé sur la manette au lancement, pendant la seconde où il ne faut rien toucher. Il
faut donc la poser à ce moment-là : c'est ce repos qui sert de référence à tout le reste.

## Après

Dans `mesures/dualsense-bt/<horodatage>/` :

| Fichier | Contenu |
|---|---|
| `correspondance.md` | le tableau commande → octet et bit |
| `trames.jsonl` | toutes les trames, horodatées à la microseconde |
| `seance.json` | manette, chemin, clé durable, transport, note |

`--note "manette n2, batterie 80%"` identifie la séance ; `--complet` demande les rapports `0x31`
avant de mesurer, pour comparer les deux formes.

## Ce que la lecture ne rate pas

Un seul fil lit la manette du début à la fin et ne fait que ça : pas d'analyse, pas d'écriture de
fichier, pas d'attente. Tout le reste travaille sur ce qu'il a accumulé. La version précédente
entrelaçait les trois, et ce qui tombait pendant qu'elle écrivait ses fichiers n'existait nulle part.
