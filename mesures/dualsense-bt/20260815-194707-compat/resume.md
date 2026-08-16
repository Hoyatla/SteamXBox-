# Séance DualSense — mesure de banc

- Date : 2026-08-15 19:47
- Transport : **Bluetooth**
- Mode : **compatibilité**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&56468dc&4&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:44464836686d`
- Rapport d'entrée : 78 octets, sortie 547, fonctionnalité 547
- Windows : Microsoft Windows NT 10.0.26200.0
- Note : controle avant seance
- Manœuvres : 1

Lecture : dans chaque tableau d'octets, **bits variés** est le masque des bits qui ont pris les deux valeurs pendant la manœuvre. Sur une manœuvre qui n'actionne qu'une commande, ce masque est le mappage de cette commande. Sur un octet d'axe il ne veut rien dire — 127 à 129 fait basculer presque tous les bits ; là, ce sont min/max qui parlent.

## 01-repos — Repos

*Consigne :* Poser la manette sur la table et NE PLUS Y TOUCHER. Retirer les mains.

*Tranche :* Valeur de repos des quatre axes, amplitude de la dérive, cadence d'une manette immobile. Décide la zone morte par défaut de Ps5ControllerDefaults et tranche « le pointeur bouge tout seul ».

### Cadence

| trames | durée | moyenne | interv. min | p50 | p90 | p99 | max | trous > 50 ms |
|---|---|---|---|---|---|---|---|---|
| 2683 | 5.0 s | 537 tr/s | 0.0 ms | 1.2 ms | 2.6 ms | 10.0 ms | 17.5 ms | 0 |

### Rapport id=0x01, 78 octets — 2683 trames

Octets qui ont varié — les autres sont restés constants sur toute la manœuvre.

| octet | 1ʳᵉ trame | min | max | valeurs | bits variés |
|---|---|---|---|---|---|
| 1 | 0x82 | 0x81 (129) | 0x82 (130) | 2 | 0x03 |
| 2 | 0x7E | 0x7D (125) | 0x7E (126) | 2 | 0x03 |
| 3 | 0x80 | 0x7F (127) | 0x80 (128) | 2 | 0xFF |
| 7 | 0x18 | 0x00 (0) | 0x3C (60) | 16 | 0x3C |

### Ce que DualSenseReportParser en fait aujourd'hui

- Trames refusées (Parse renvoie null) : **0** sur 2683.
- Stick gauche X [0.00 … 0.00], Y [-0.00 … 0.00] ; stick droit X [0.00 … 0.00], Y [-0.00 … 0.00].
- Gâchettes décodées, maximum atteint : L=0.00 R=0.00.
- États de boutons décodés, du plus fréquent au moins fréquent :

| boutons | trames |
|---|---|
| — | 2683 |

