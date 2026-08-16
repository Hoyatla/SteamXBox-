# Séance DualSense — mesure de banc

- Date : 2026-08-15 20:05
- Transport : **Bluetooth**
- Mode : **compatibilité**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&56468dc&4&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:44464836686d`
- Rapport d'entrée : 78 octets, sortie 547, fonctionnalité 547
- Windows : Microsoft Windows NT 10.0.26200.0
- Manœuvres : 13

Lecture : dans chaque tableau d'octets, **bits variés** est le masque des bits qui ont pris les deux valeurs pendant la manœuvre. Sur une manœuvre qui n'actionne qu'une commande, ce masque est le mappage de cette commande. Sur un octet d'axe il ne veut rien dire — 127 à 129 fait basculer presque tous les bits ; là, ce sont min/max qui parlent.

## 01-repos — Repos

*Consigne :* Poser la manette sur la table et NE PLUS Y TOUCHER. Retirer les mains. 12 secondes.

*Tranche :* Valeur de repos des quatre axes, amplitude de la dérive, cadence d'une manette immobile. Décide la zone morte par défaut de Ps5ControllerDefaults et tranche « le pointeur bouge tout seul ».

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7289 | 12.1 s | 601 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 17.5 ms | 0 |

### Rapport id=0x01, 78 octets — 7289 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 4 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x04 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7289.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7289 |

## 02-stick-gauche-tenu — Stick gauche poussé et maintenu

*Consigne :* Pousser le stick gauche à fond vers la DROITE, le tenir immobile 8 à 10 secondes, puis relâcher.

*Tranche :* LA question du pointeur saccadé. Le pointeur est piloté en vitesse : si la manette n'émet plus tant que le stick ne bouge pas, chaque silence arrête le curseur. Regarder l'intervalle maximal entre trames, pas la moyenne.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 5204 | 8.8 s | 595 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 122.5 ms | 1 |

### Rapport id=0x01, 78 octets — 5204 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x7D (125) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x7F | 0x73 (115) | 0x94 (148) | 34 | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x24 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 5204.
- Stick gauche X [0.00 … 0.99], Y [-0.16 … 0.10] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 5204 |

## 03-stick-gauche-lent — Stick gauche, cercles lents

*Consigne :* Décrire des cercles LENTS avec le stick gauche pendant une dizaine de secondes, puis relâcher.

*Tranche :* Cadence pendant un mouvement lent continu, et pas de trous. C'est le régime réel du pointeur.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7372 | 12.4 s | 594 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 131.6 ms | 1 |

### Rapport id=0x01, 78 octets — 7372 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x7E | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x1C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7372.
- Stick gauche X [-1.00 … 0.99], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7372 |

