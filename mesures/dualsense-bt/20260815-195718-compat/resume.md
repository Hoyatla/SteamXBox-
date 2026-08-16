# Séance DualSense — mesure de banc

- Date : 2026-08-15 19:57
- Transport : **Bluetooth**
- Mode : **compatibilité**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&56468dc&4&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:44464836686d`
- Rapport d'entrée : 78 octets, sortie 547, fonctionnalité 547
- Windows : Microsoft Windows NT 10.0.26200.0
- Manœuvres : 13

Lecture : dans chaque tableau d'octets, **bits variés** est le masque des bits qui ont pris les deux valeurs pendant la manœuvre. Sur une manœuvre qui n'actionne qu'une commande, ce masque est le mappage de cette commande. Sur un octet d'axe il ne veut rien dire — 127 à 129 fait basculer presque tous les bits ; là, ce sont min/max qui parlent.

## 01-repos — Repos

*Consigne :* Poser la manette sur la table et NE PLUS Y TOUCHER. Retirer les mains.

*Tranche :* Valeur de repos des quatre axes, amplitude de la dérive, cadence d'une manette immobile. Décide la zone morte par défaut de Ps5ControllerDefaults et tranche « le pointeur bouge tout seul ».

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 12214 | 20.0 s | 611 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.9 ms | 20.1 ms | 0 |

### Rapport id=0x01, 78 octets — 12214 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x30 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 12214.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 12214 |

## 02-stick-gauche-tenu — Stick gauche poussé et maintenu

*Consigne :* Pousser le stick gauche à fond vers la DROITE et l'y maintenir immobile, sans rien toucher d'autre.

*Tranche :* LA question du pointeur saccadé. Le pointeur est piloté en vitesse : si la manette n'émet plus tant que le stick ne bouge pas, chaque silence arrête le curseur. Regarder l'intervalle maximal entre trames, pas la moyenne.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7646 | 12.0 s | 637 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 13.8 ms | 0 |

### Rapport id=0x01, 78 octets — 7646 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0xFF | 0x7D (125) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x75 | 0x6D (109) | 0x85 (133) | 25 | 0xFF |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x00 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7646.
- Stick gauche X [0.00 … 0.99], Y [-0.04 … 0.15] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7646 |

## 03-stick-gauche-lent — Stick gauche, cercles lents

*Consigne :* Décrire des cercles LENTS avec le stick gauche, environ un tour toutes les trois secondes.

*Tranche :* Cadence pendant un mouvement lent continu, et pas de trous. C'est le régime réel du pointeur.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9386 | 15.0 s | 626 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 14.0 ms | 0 |

### Rapport id=0x01, 78 octets — 9386 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0xFF | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x65 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x38 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9386.
- Stick gauche X [-1.00 … 0.99], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 9386 |

## 04-stick-gauche-extremes — Stick gauche, huit directions

*Consigne :* Stick gauche à fond, 2 s par direction, en revenant au centre entre chaque : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales.

*Tranche :* Min/max réels par axe et forme du débattement (carré ou cercle). Décide la normalisation et si une diagonale atteint 1,0 ou seulement 0,7.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9989 | 16.0 s | 624 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 21.3 ms | 0 |

### Rapport id=0x01, 78 octets — 9989 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x02 (2) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x7E | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 7 | 0x0C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9989.
- Stick gauche X [-0.98 … 0.99], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 9989 |

## 05-stick-droit-extremes — Stick droit, huit directions

*Consigne :* Même chose avec le stick DROIT : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales.

*Tranche :* Idem pour le second stick, et confirme les offsets d'axes 3 et 4 du rapport.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9918 | 16.0 s | 620 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 18.8 ms | 0 |

### Rapport id=0x01, 78 octets — 9918 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x80 | 0x04 (4) | 0xF8 (248) | 64+ | 0xFF |
| 4 | 0x82 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 7 | 0x0C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9918.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.97 … 0.94], Y [-0.99 … 1.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 9918 |

## 06-gachettes — Gâchettes, course lente

*Consigne :* Presser L2 TRÈS LENTEMENT de 0 à fond puis relâcher (6 s). Attendre. Puis pareil avec R2.

*Tranche :* Les gâchettes sont-elles analogiques en Bluetooth compat, ou seulement tout-ou-rien ? Le code place les gâchettes en octets 8/9 du rapport compact, la documentation interne dit « pas de gâchettes analogiques en compat ». Les deux ne peuvent pas être vraies.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9971 | 16.0 s | 623 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 18.8 ms | 0 |

### Rapport id=0x01, 78 octets — 9971 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 6 | 0x00 | 0x00 (0) | 0x08 (8) | 3 | 0x0C |
| 7 | 0x38 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |
| 8 | 0x00 | 0x00 (0) | 0xFF (255) | 2 | 0xFF |
| 9 | 0x00 | 0x00 (0) | 0xFF (255) | 2 | 0xFF |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9971.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=1.00 R=1.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 9971 |

## 07-boutons-face — Boutons de face, un par un

*Consigne :* Presser 2 s puis RELÂCHER, en attendant entre chaque : CROIX, ROND, CARRÉ, TRIANGLE.

*Tranche :* Confirme par la mesure le quartet haut de l'octet boutons (0x20/0x40/0x10/0x80).

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9976 | 16.0 s | 624 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 21.3 ms | 0 |

### Rapport id=0x01, 78 octets — 9976 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7F | 0x7C (124) | 0x80 (128) | 5 | 0xFF |
| 4 | 0x82 | 0x82 (130) | 0x87 (135) | 6 | 0x07 |
| 5 | 0x08 | 0x08 (8) | 0x88 (136) | 5 | 0xF0 |
| 7 | 0x04 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9976.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.03 … 0.00], Y [-0.05 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 6119 |
| Y | 1115 |
| X | 1011 |
| A | 932 |
| B | 799 |

## 08-croix-directionnelle — Croix directionnelle

*Consigne :* Presser 2 s puis relâcher : HAUT, DROITE, BAS, GAUCHE, puis HAUT+DROITE, puis BAS+GAUCHE.

*Tranche :* Confirme que la croix est bien un hat 0-7 avec 8 au centre, et non un champ de bits.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 9992 | 16.0 s | 624 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 17.5 ms | 0 |

### Rapport id=0x01, 78 octets — 9992 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7E | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 4 | 0x83 | 0x82 (130) | 0x83 (131) | 2 | 0x01 |
| 5 | 0x08 | 0x00 (0) | 0x08 (8) | 6 | 0x0F |
| 7 | 0x20 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 9992.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 6205 |
| DPadDown | 1242 |
| DPadLeft | 772 |
| DPadRight | 759 |
| DPadUp | 653 |
| DPadUp, DPadRight | 361 |

