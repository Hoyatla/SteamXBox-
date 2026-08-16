# Séance DualSense — mesure de banc

- Date : 2026-08-15 22:12
- Transport : **Bluetooth**
- Mode : **compatibilité**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&15f755c8&3&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:90b685f7696f`
- Rapport d'entrée : 78 octets, sortie 547, fonctionnalité 547
- Windows : Microsoft Windows NT 10.0.26200.0
- Manœuvres : 14

Lecture : dans chaque tableau d'octets, **bits variés** est le masque des bits qui ont pris les deux valeurs pendant la manœuvre. Sur une manœuvre qui n'actionne qu'une commande, ce masque est le mappage de cette commande. Sur un octet d'axe il ne veut rien dire — 127 à 129 fait basculer presque tous les bits ; là, ce sont min/max qui parlent.

## 01-repos — Repos

*Consigne :* Poser la manette sur la table et NE PLUS Y TOUCHER. Retirer les mains. 12 secondes.

*Tranche :* Valeur de repos des quatre axes, amplitude de la dérive, cadence d'une manette immobile. Décide la zone morte par défaut de Ps5ControllerDefaults et tranche « le pointeur bouge tout seul ».

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 6997 | 12.1 s | 577 tr/s | 0.9 ms | 1.2 ms | 1.3 ms | 8.9 ms | 18.8 ms | 0 |

### Rapport id=0x01, 78 octets — 6997 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 2 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x83 | 0x82 (130) | 0x83 (131) | 2 | 0x01 |
| 7 | 0x28 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 6997.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 6997 |

## 02-stick-gauche-tenu — Stick gauche poussé et maintenu

*Consigne :* Pousser le stick gauche à fond vers la DROITE, le tenir immobile 8 à 10 secondes, puis relâcher.

*Tranche :* LA question du pointeur saccadé. Le pointeur est piloté en vitesse : si la manette n'émet plus tant que le stick ne bouge pas, chaque silence arrête le curseur. Regarder l'intervalle maximal entre trames, pas la moyenne.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 2496 | 4.3 s | 580 tr/s | 0.4 ms | 1.2 ms | 1.3 ms | 8.8 ms | 13.8 ms | 0 |

### Rapport id=0x01, 78 octets — 2496 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x7F (127) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x80 | 0x7E (126) | 0x8E (142) | 17 | 0xFF |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x83 | 0x82 (130) | 0x83 (131) | 2 | 0x01 |
| 7 | 0x3C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 2496.
- Stick gauche X [0.00 … 0.99], Y [-0.11 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 2496 |

## 03-stick-gauche-lent — Stick gauche, cercles lents

*Consigne :* Décrire des cercles LENTS avec le stick gauche pendant une dizaine de secondes, puis relâcher.

*Tranche :* Cadence pendant un mouvement lent continu, et pas de trous. C'est le régime réel du pointeur.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 8106 | 13.7 s | 593 tr/s | 0.9 ms | 1.2 ms | 1.3 ms | 8.8 ms | 13.9 ms | 0 |

### Rapport id=0x01, 78 octets — 8106 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x7F | 0x00 (0) | 0xFB (251) | 64+ | 0xFF |
| 2 | 0x80 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x83 | 0x82 (130) | 0x83 (131) | 2 | 0x01 |
| 7 | 0x20 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 8106.
- Stick gauche X [-1.00 … 0.96], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 8106 |

## 04-stick-gauche-extremes — Stick gauche, huit directions

*Consigne :* Stick gauche à fond, en revenant au centre entre chaque : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales. Marquer un temps au centre.

*Tranche :* Min/max réels par axe et forme du débattement (carré ou cercle). Décide la normalisation et si une diagonale atteint 1,0 ou seulement 0,7.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7322 | 12.4 s | 588 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 15.0 ms | 0 |

### Rapport id=0x01, 78 octets — 7322 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x01 (1) | 0xFB (251) | 64+ | 0xFF |
| 2 | 0x7F | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x82 | 0x82 (130) | 0x83 (131) | 2 | 0x01 |
| 7 | 0x1C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7322.
- Stick gauche X [-0.99 … 0.96], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7322 |

## 05-stick-droit-extremes — Stick droit, huit directions

*Consigne :* Même chose avec le stick DROIT : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales.

*Tranche :* Idem pour le second stick, et confirme les offsets d'axes 3 et 4 du rapport.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 13862 | 24.0 s | 578 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 20.0 ms | 0 |

### Rapport id=0x01, 78 octets — 13862 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x81 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 3 | 0x7F | 0x0B (11) | 0xF8 (248) | 64+ | 0xFF |
| 4 | 0x82 | 0x04 (4) | 0xF9 (249) | 64+ | 0xFF |
| 7 | 0x08 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 13862.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.91 … 0.94], Y [-0.95 … 0.97].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 13862 |

## 06-gachettes — Gâchettes, course lente

*Consigne :* Presser L2 TRÈS LENTEMENT de 0 à fond puis relâcher. Attendre. Puis pareil avec R2.

*Tranche :* Les gâchettes sont-elles analogiques en Bluetooth compat, ou seulement tout-ou-rien ? Le code place les gâchettes en octets 8/9 du rapport compact, la documentation interne dit « pas de gâchettes analogiques en compat ». Les deux ne peuvent pas être vraies.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 5699 | 10.0 s | 568 tr/s | 0.8 ms | 1.2 ms | 2.5 ms | 10.0 ms | 18.8 ms | 0 |

### Rapport id=0x01, 78 octets — 5699 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x82 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 6 | 0x00 | 0x00 (0) | 0x08 (8) | 3 | 0x0C |
| 7 | 0x1C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |
| 8 | 0x00 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 9 | 0x00 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 5699.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=1.00 R=1.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 5699 |

## 07-boutons-face — Boutons de face, un par un

*Consigne :* Presser puis RELÂCHER, en marquant un temps entre chaque : CROIX, ROND, CARRÉ, TRIANGLE.

*Tranche :* Confirme par la mesure le quartet haut de l'octet boutons (0x20/0x40/0x10/0x80).

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 3439 | 5.9 s | 584 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 15.1 ms | 0 |

### Rapport id=0x01, 78 octets — 3439 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x82 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 5 | 0x08 | 0x08 (8) | 0x88 (136) | 5 | 0xF0 |
| 7 | 0x38 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 3439.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 3021 |
| A | 154 |
| B | 104 |
| X | 85 |
| Y | 75 |

## 08-croix-directionnelle — Croix directionnelle

*Consigne :* Presser puis relâcher : HAUT, DROITE, BAS, GAUCHE, puis HAUT+DROITE, puis BAS+GAUCHE.

*Tranche :* Confirme que la croix est bien un hat 0-7 avec 8 au centre, et non un champ de bits.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 33968 | 57.5 s | 591 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.9 ms | 21.3 ms | 0 |

### Rapport id=0x01, 78 octets — 33968 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x82 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 5 | 0x08 | 0x00 (0) | 0x08 (8) | 9 | 0x0F |
| 7 | 0x30 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 33968.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 22767 |
| DPadLeft | 6021 |
| DPadDown, DPadLeft | 2944 |
| DPadDown | 1048 |
| DPadRight | 341 |
| DPadUp, DPadRight | 315 |
| DPadDown, DPadRight | 249 |
| DPadUp | 152 |
| DPadUp, DPadLeft | 131 |

## 09-epaules-et-sticks — L1, R1, L3, R3

*Consigne :* Presser puis relâcher, un par un : L1, R1, puis CLIC du stick gauche (L3), puis CLIC du stick droit (R3).

*Tranche :* Confirme les bits 0x01/0x02/0x40/0x80 de l'octet épaules.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 14147 | 24.6 s | 575 tr/s | 0.0 ms | 1.2 ms | 2.5 ms | 10.0 ms | 20.1 ms | 0 |

### Rapport id=0x01, 78 octets — 14147 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x6D (109) | 0x89 (137) | 29 | 0xFF |
| 2 | 0x83 | 0x64 (100) | 0x86 (134) | 35 | 0xFF |
| 3 | 0x7F | 0x77 (119) | 0x81 (129) | 11 | 0xFF |
| 4 | 0x82 | 0x7A (122) | 0x85 (133) | 12 | 0xFF |
| 6 | 0x00 | 0x00 (0) | 0x40 (64) | 5 | 0x43 |
| 7 | 0x24 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 14147.
- Stick gauche X [-0.15 … 0.07], Y [-0.05 … 0.22] ; stick droit X [-0.07 … 0.00], Y [-0.04 … 0.05].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7292 |
| LeftBumper | 6025 |
| RightBumper | 667 |
| LeftStick | 105 |
| LeftBumper, RightBumper | 58 |

## 10-boutons-systeme — Create, Options, PS, Muet, clic pavé

*Consigne :* Presser puis relâcher, un par un, bien séparés : CREATE (petit bouton de GAUCHE), OPTIONS (petit bouton de DROITE), PS, MUET (sous le PS), puis un CLIC franc sur le pavé tactile.

*Tranche :* La manœuvre la plus importante du lot. Le code affirme que le troisième octet de boutons n'existe pas en Bluetooth compat — un compteur de trames occupe sa place, et le lire comme un bouton faisait basculer le mode deux fois par seconde. Si un bit bouge ici et nulle part ailleurs, la DualSense retrouve un bouton de changement de mode ; sinon la question est close et le mode restera sur l'accord L3+R3. Rien ne sera recâblé sans cette trace.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 38587 | 67.2 s | 574 tr/s | 0.0 ms | 1.2 ms | 2.5 ms | 10.0 ms | 47.2 ms | 0 |

### Rapport id=0x01, 78 octets — 38587 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x7E (126) | 0x83 (131) | 6 | 0xFF |
| 2 | 0x80 | 0x7D (125) | 0x83 (131) | 7 | 0xFF |
| 3 | 0x7F | 0x3B (59) | 0x96 (150) | 56 | 0xFF |
| 4 | 0x81 | 0x0C (12) | 0xAD (173) | 59 | 0xFF |
| 6 | 0x00 | 0x00 (0) | 0x30 (48) | 4 | 0x30 |
| 7 | 0x0C | 0x00 (0) | 0x3E (62) | 48 | 0x3F |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 38587.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.54 … 0.17], Y [-0.35 … 0.91].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 37565 |
| View | 481 |
| Menu | 283 |
| Menu, View | 258 |

## 11-pave-tactile — Pavé tactile, glissé

*Consigne :* Glisser UN doigt lentement sur le pavé tactile : gauche→droite. Retirer le doigt. Puis haut→bas. Ne pas cliquer.

*Tranche :* Le pavé remonte-t-il des coordonnées en compat ? Si oui, la DualSense gagne un pointeur absolu ; sinon la famille PS5 reste au stick.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 15431 | 26.7 s | 579 tr/s | 0.3 ms | 1.2 ms | 1.4 ms | 10.0 ms | 18.8 ms | 0 |

### Rapport id=0x01, 78 octets — 15431 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x82 (130) | 3 | 0x03 |
| 2 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 3 | 0x7F | 0x7D (125) | 0x9A (154) | 30 | 0xFF |
| 4 | 0x85 | 0x84 (132) | 0x98 (152) | 21 | 0x1F |
| 7 | 0x1C | 0x00 (0) | 0x3E (62) | 32 | 0x3E |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 15431.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.20], Y [-0.19 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 15431 |

## 12-mouvement-manette — Manette secouée, aucune commande touchée

*Consigne :* Prendre la manette et la bouger dans l'espace pendant quelques secondes : incliner, tourner, secouer doucement. NE toucher NI stick NI bouton NI gâchette. Puis la reposer.

*Tranche :* Gyroscope et accéléromètre : présents en compat ou seulement en 0x31 ? Sert aussi de témoin — des octets qui bougent ici et qui bougeaient déjà au repos sont du bruit, pas des données.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 5849 | 10.2 s | 573 tr/s | 0.0 ms | 1.2 ms | 1.4 ms | 10.0 ms | 29.1 ms | 0 |

### Rapport id=0x01, 78 octets — 5849 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x80 | 0x7C (124) | 0x82 (130) | 7 | 0xFF |
| 4 | 0x85 | 0x7F (127) | 0x85 (133) | 7 | 0xFF |
| 7 | 0x20 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 5849.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.03 … 0.00], Y [-0.04 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 5849 |

## 14-ps-muet-pave — PS, Muet, clic pavé — départage

*Consigne :* Deux fois PS (attendre entre les deux). PAUSE de 3 secondes. Deux fois MUET. PAUSE de 3 secondes. Deux CLICS francs sur le pavé tactile. Rien d'autre, jamais deux en même temps.

*Tranche :* La séance du 15 août a montré que le bit 0x01 de l'octet 7 est un vrai bouton système — il ne bouge dans aucune autre manœuvre. Elle n'a pas pu dire lequel des trois le produit, la manœuvre 10 ayant duré 78 secondes d'essais mêlés. Ici l'ordre des épisodes dans le temps donne la réponse : deux paires groupées, et celle qui manque est le bouton que la manette n'émet pas dans cette forme de rapport. C'est ce qui décide si la DualSense retrouve un bouton de changement de mode.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 22970 | 39.3 s | 584 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 21.3 ms | 0 |

### Rapport id=0x01, 78 octets — 22970 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 4 | 0x83 | 0x82 (130) | 0x8B (139) | 10 | 0x0F |
| 6 | 0x00 | 0x00 (0) | 0x20 (32) | 2 | 0x20 |
| 7 | 0x08 | 0x00 (0) | 0x3E (62) | 48 | 0x3F |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 22970.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.09 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 22483 |
| Menu | 487 |

## 13-repos-final — Repos, seconde fois

*Consigne :* Reposer la manette et ne plus y toucher. 12 secondes.

*Tranche :* Témoin de fin de séance. Comparé au 01, il dit si la manette a dérivé pendant la séance et si un octet a changé d'état de façon durable.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7336 | 12.1 s | 604 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 64.7 ms | 1 |

### Rapport id=0x01, 78 octets — 7336 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 2 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 3 | 0x7F | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 7 | 0x38 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7336.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.03 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7336 |

