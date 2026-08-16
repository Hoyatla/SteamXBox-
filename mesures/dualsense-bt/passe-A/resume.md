# Séance DualSense — mesure de banc

- Date : 2026-08-15 19:52
- Transport : **Bluetooth**
- Mode : **compatibilité**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&56468dc&4&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:44464836686d`
- Rapport d'entrée : 78 octets, sortie 547, fonctionnalité 547
- Windows : Microsoft Windows NT 10.0.26200.0
- Note : passe A compat, BT integre
- Manœuvres : 1

Lecture : dans chaque tableau d'octets, **bits variés** est le masque des bits qui ont pris les deux valeurs pendant la manœuvre. Sur une manœuvre qui n'actionne qu'une commande, ce masque est le mappage de cette commande. Sur un octet d'axe il ne veut rien dire — 127 à 129 fait basculer presque tous les bits ; là, ce sont min/max qui parlent.

## 01-repos — Repos

*Consigne :* Poser la manette sur la table et NE PLUS Y TOUCHER. Retirer les mains.

*Tranche :* Valeur de repos des quatre axes, amplitude de la dérive, cadence d'une manette immobile. Décide la zone morte par défaut de Ps5ControllerDefaults et tranche « le pointeur bouge tout seul ».

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 12046 | 20.0 s | 602 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 20.0 ms | 0 |

### Rapport id=0x01, 78 octets — 12046 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x82 | 0x7F (127) | 0x82 (130) | 4 | 0xFF |
| 2 | 0x7E | 0x7B (123) | 0x80 (128) | 6 | 0xFF |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x08 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 12046.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.04] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 12046 |

## 02-stick-gauche-tenu — Stick gauche poussé et maintenu

*Consigne :* Pousser le stick gauche à fond vers la DROITE et l'y maintenir immobile, sans rien toucher d'autre.

*Tranche :* LA question du pointeur saccadé. Le pointeur est piloté en vitesse : si la manette n'émet plus tant que le stick ne bouge pas, chaque silence arrête le curseur. Regarder l'intervalle maximal entre trames, pas la moyenne.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7419 | 12.0 s | 618 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 15.0 ms | 0 |

### Rapport id=0x01, 78 octets — 7419 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 7 | 0x20 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7419.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7419 |

