# Séance DualSense — mesure de banc

- Date : 2026-08-15 20:07
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
| 7265 | 12.1 s | 599 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 17.5 ms | 0 |

### Rapport id=0x01, 78 octets — 7265 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x7F | 0x7F (127) | 0x81 (129) | 3 | 0xFF |
| 2 | 0x80 | 0x7E (126) | 0x80 (128) | 3 | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x20 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7265.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7265 |

## 02-stick-gauche-tenu — Stick gauche poussé et maintenu

*Consigne :* Pousser le stick gauche à fond vers la DROITE, le tenir immobile 8 à 10 secondes, puis relâcher.

*Tranche :* LA question du pointeur saccadé. Le pointeur est piloté en vitesse : si la manette n'émet plus tant que le stick ne bouge pas, chaque silence arrête le curseur. Regarder l'intervalle maximal entre trames, pas la moyenne.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 6723 | 11.4 s | 591 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 132.3 ms | 1 |

### Rapport id=0x01, 78 octets — 6723 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x7E (126) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x81 | 0x73 (115) | 0x83 (131) | 17 | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x08 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 6723.
- Stick gauche X [0.00 … 0.99], Y [-0.00 … 0.10] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 6723 |

## 03-stick-gauche-lent — Stick gauche, cercles lents

*Consigne :* Décrire des cercles LENTS avec le stick gauche pendant une dizaine de secondes, puis relâcher.

*Tranche :* Cadence pendant un mouvement lent continu, et pas de trous. C'est le régime réel du pointeur.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7468 | 12.6 s | 594 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 122.1 ms | 1 |

### Rapport id=0x01, 78 octets — 7468 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x7F | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x0C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7468.
- Stick gauche X [-1.00 … 0.99], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7468 |

## 04-stick-gauche-extremes — Stick gauche, huit directions

*Consigne :* Stick gauche à fond, en revenant au centre entre chaque : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales. Marquer un temps au centre.

*Tranche :* Min/max réels par axe et forme du débattement (carré ou cercle). Décide la normalisation et si une diagonale atteint 1,0 ou seulement 0,7.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 23084 | 38.5 s | 600 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 126.0 ms | 1 |

### Rapport id=0x01, 78 octets — 23084 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x01 (1) | 0xFF (255) | 64+ | 0xFF |
| 2 | 0x7E | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 3 | 0x7F | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 7 | 0x1C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 23084.
- Stick gauche X [-0.99 … 0.99], Y [-0.99 … 1.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 23084 |

## 05-stick-droit-extremes — Stick droit, huit directions

*Consigne :* Même chose avec le stick DROIT : HAUT, BAS, GAUCHE, DROITE, puis les quatre diagonales.

*Tranche :* Idem pour le second stick, et confirme les offsets d'axes 3 et 4 du rapport.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 18296 | 30.5 s | 601 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 135.9 ms | 1 |

### Rapport id=0x01, 78 octets — 18296 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x81 | 0x03 (3) | 0xF8 (248) | 64+ | 0xFF |
| 4 | 0x82 | 0x00 (0) | 0xFF (255) | 64+ | 0xFF |
| 7 | 0x08 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 18296.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.98 … 0.94], Y [-0.99 … 1.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 18296 |

## 06-gachettes — Gâchettes, course lente

*Consigne :* Presser L2 TRÈS LENTEMENT de 0 à fond puis relâcher. Attendre. Puis pareil avec R2.

*Tranche :* Les gâchettes sont-elles analogiques en Bluetooth compat, ou seulement tout-ou-rien ? Le code place les gâchettes en octets 8/9 du rapport compact, la documentation interne dit « pas de gâchettes analogiques en compat ». Les deux ne peuvent pas être vraies.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7017 | 12.2 s | 577 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 121.4 ms | 1 |

### Rapport id=0x01, 78 octets — 7017 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7E | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 6 | 0x00 | 0x00 (0) | 0x08 (8) | 3 | 0x0C |
| 7 | 0x24 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |
| 8 | 0x00 | 0x00 (0) | 0xFF (255) | 2 | 0xFF |
| 9 | 0x00 | 0x00 (0) | 0xFF (255) | 3 | 0xFF |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7017.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=1.00 R=1.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7017 |

## 07-boutons-face — Boutons de face, un par un

*Consigne :* Presser puis RELÂCHER, en marquant un temps entre chaque : CROIX, ROND, CARRÉ, TRIANGLE.

*Tranche :* Confirme par la mesure le quartet haut de l'octet boutons (0x20/0x40/0x10/0x80).

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 3735 | 6.5 s | 577 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 160.4 ms | 1 |

### Rapport id=0x01, 78 octets — 3735 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7F | 0x7C (124) | 0x7F (127) | 4 | 0x03 |
| 4 | 0x81 | 0x81 (129) | 0x88 (136) | 8 | 0x0F |
| 5 | 0x08 | 0x08 (8) | 0x88 (136) | 5 | 0xF0 |
| 7 | 0x30 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 3735.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.03 … 0.00], Y [-0.06 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 3260 |
| A | 145 |
| B | 131 |
| X | 114 |
| Y | 85 |

## 08-croix-directionnelle — Croix directionnelle

*Consigne :* Presser puis relâcher : HAUT, DROITE, BAS, GAUCHE, puis HAUT+DROITE, puis BAS+GAUCHE.

*Tranche :* Confirme que la croix est bien un hat 0-7 avec 8 au centre, et non un champ de bits.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 16673 | 28.6 s | 584 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 129.7 ms | 1 |

### Rapport id=0x01, 78 octets — 16673 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x80 (128) | 0x81 (129) | 2 | 0x01 |
| 3 | 0x7E | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 5 | 0x08 | 0x00 (0) | 0x08 (8) | 5 | 0x0E |
| 7 | 0x1C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 16673.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 12578 |
| DPadDown | 2205 |
| DPadLeft | 996 |
| DPadRight | 452 |
| DPadUp | 442 |

## 09-epaules-et-sticks — L1, R1, L3, R3

*Consigne :* Presser puis relâcher, un par un : L1, R1, puis CLIC du stick gauche (L3), puis CLIC du stick droit (R3).

*Tranche :* Confirme les bits 0x01/0x02/0x40/0x80 de l'octet épaules.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 19143 | 32.8 s | 584 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.9 ms | 124.8 ms | 1 |

### Rapport id=0x01, 78 octets — 19143 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x80 | 0x7C (124) | 0xAB (171) | 44 | 0xFF |
| 2 | 0x81 | 0x52 (82) | 0x82 (130) | 49 | 0xFF |
| 3 | 0x81 | 0x77 (119) | 0x8E (142) | 24 | 0xFF |
| 4 | 0x82 | 0x74 (116) | 0x88 (136) | 21 | 0xFF |
| 6 | 0x00 | 0x00 (0) | 0x80 (128) | 5 | 0xC3 |
| 7 | 0x0C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 19143.
- Stick gauche X [-0.03 … 0.34], Y [-0.00 … 0.36] ; stick droit X [-0.07 … 0.11], Y [-0.06 … 0.09].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 10124 |
| LeftBumper | 7630 |
| RightStick | 658 |
| RightBumper | 570 |
| LeftStick | 161 |

## 10-boutons-systeme — Create, Options, PS, Muet, clic pavé

*Consigne :* Presser puis relâcher, un par un, bien séparés : CREATE (petit bouton de GAUCHE), OPTIONS (petit bouton de DROITE), PS, MUET (sous le PS), puis un CLIC franc sur le pavé tactile.

*Tranche :* La manœuvre la plus importante du lot. Le code affirme que le troisième octet de boutons n'existe pas en Bluetooth compat — un compteur de trames occupe sa place, et le lire comme un bouton faisait basculer le mode deux fois par seconde. Si un bit bouge ici et nulle part ailleurs, la DualSense retrouve un bouton de changement de mode ; sinon la question est close et le mode restera sur l'accord L3+R3. Rien ne sera recâblé sans cette trace.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 46115 | 78.6 s | 587 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 10.0 ms | 128.8 ms | 2 |

### Rapport id=0x01, 78 octets — 46115 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x81 | 0x7F (127) | 0x85 (133) | 7 | 0xFF |
| 2 | 0x7E | 0x78 (120) | 0x7E (126) | 7 | 0x07 |
| 3 | 0x81 | 0x78 (120) | 0x96 (150) | 30 | 0xFF |
| 4 | 0x81 | 0x7E (126) | 0x8A (138) | 13 | 0xFF |
| 6 | 0x00 | 0x00 (0) | 0x20 (32) | 3 | 0x30 |
| 7 | 0x28 | 0x00 (0) | 0x3E (62) | 48 | 0x3F |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 46115.
- Stick gauche X [0.00 … 0.04], Y [-0.00 … 0.06] ; stick droit X [-0.06 … 0.17], Y [-0.08 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 45821 |
| View | 169 |
| Menu | 125 |

## 11-pave-tactile — Pavé tactile, glissé

*Consigne :* Glisser UN doigt lentement sur le pavé tactile : gauche→droite. Retirer le doigt. Puis haut→bas. Ne pas cliquer.

*Tranche :* Le pavé remonte-t-il des coordonnées en compat ? Si oui, la DualSense gagne un pointeur absolu ; sinon la famille PS5 reste au stick.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 13247 | 22.7 s | 584 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 9.9 ms | 123.3 ms | 1 |

### Rapport id=0x01, 78 octets — 13247 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x82 | 0x7E (126) | 0x82 (130) | 5 | 0xFF |
| 2 | 0x7E | 0x7D (125) | 0x81 (129) | 5 | 0xFF |
| 3 | 0x81 | 0x78 (120) | 0x82 (130) | 11 | 0xFF |
| 4 | 0x81 | 0x81 (129) | 0x8D (141) | 13 | 0x0F |
| 7 | 0x30 | 0x00 (0) | 0x3E (62) | 32 | 0x3E |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 13247.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.06 … 0.00], Y [-0.10 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 13247 |

## 12-mouvement-manette — Manette secouée, aucune commande touchée

*Consigne :* Prendre la manette et la bouger dans l'espace pendant quelques secondes : incliner, tourner, secouer doucement. NE toucher NI stick NI bouton NI gâchette. Puis la reposer.

*Tranche :* Gyroscope et accéléromètre : présents en compat ou seulement en 0x31 ? Sert aussi de témoin — des octets qui bougent ici et qui bougeaient déjà au repos sont du bruit, pas des données.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 1993 | 3.6 s | 557 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.8 ms | 122.4 ms | 1 |

### Rapport id=0x01, 78 octets — 1993 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 2 | 0x7E | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 3 | 0x7F | 0x7C (124) | 0x9B (155) | 32 | 0xFF |
| 4 | 0x81 | 0x7A (122) | 0x9F (159) | 34 | 0xFF |
| 7 | 0x28 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 1993.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.03 … 0.21], Y [-0.24 … 0.05].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 1993 |

## 13-repos-final — Repos, seconde fois

*Consigne :* Reposer la manette et ne plus y toucher. 12 secondes.

*Tranche :* Témoin de fin de séance. Comparé au 01, il dit si la manette a dérivé pendant la séance et si un octet a changé d'état de façon durable.

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 7118 | 12.1 s | 587 tr/s | 0.0 ms | 1.2 ms | 1.3 ms | 8.9 ms | 21.7 ms | 0 |

### Rapport id=0x01, 78 octets — 7118 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 2 | 0x7E | 0x7E (126) | 0x7F (127) | 2 | 0x01 |
| 3 | 0x80 | 0x7C (124) | 0x80 (128) | 5 | 0xFF |
| 4 | 0x80 | 0x7E (126) | 0x80 (128) | 3 | 0xFF |
| 7 | 0x2C | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 7118.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [-0.03 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 7118 |

