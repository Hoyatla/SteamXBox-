# Correspondance des commandes

- Date : 2026-08-15 23:06
- Transport : **Bluetooth**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&15f755c8&3&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:90b685f7696f`
- Rapport d'entrée : 78 octets

| Commande | Ce qui bouge dans le rapport | tout ce qui a sauté |
|---|---|---|
| Croix | octet 5 · bit 0x20 | o5:8→40 |
| Rond | octet 5 · 8 → 72 | o5:8→72 |
| Carré | octet 5 · bit 0x10 · octet 4 · bit 0x02 | o5:8→24, o4:132→133 |
| Triangle | octet 5 · 8 → 136 | o5:8→136 |
| L1 | octet 6 · bit 0x01 | o6:0→1 |
| R1 | octet 6 · bit 0x02 | o6:0→2 |
| L2 | octet 8 · 0 → 255 · octet 6 · bit 0x04 | o8:0→255, o6:0→4 |
| R2 | octet 9 · 0 → 255 · octet 6 · bit 0x08 | o9:0→255, o6:0→8 |
| L3 | octet 6 · 0 → 64 · octet 2 · bit 0x7F | o6:0→64, o2:128→127, o1:128→129 |
| R3 | octet 6 · 0 → 128 · octet 4 · bit 0x10 | o6:0→128, o4:131→130, o3:130→129 |
| Create | octet 6 · bit 0x10 | o6:0→16 |
| Options | octet 6 · bit 0x20 · octet 4 · 131 → 130 | o6:0→32, o4:131→130 |
| PS | **rien détecté** | — |
| Muet | **rien détecté** | — |
| Clic du pavé tactile | octet 3 · 130 → 129 | o3:130→129 |
| Doigt glissé sur le pavé | **rien détecté** | — |
| Croix directionnelle HAUT | octet 5 · 8 → 0 | o5:8→0 |
| Croix directionnelle DROITE | octet 5 · bit 0x02 | o5:8→2 |
| Croix directionnelle BAS | octet 5 · bit 0x04 | o5:8→4 |
| Croix directionnelle GAUCHE | octet 5 · bit 0x06 | o5:8→6 |
| Stick gauche à DROITE | octet 1 · bit 0x7A · octet 2 · bit 0x7F | o1:128→129, o2:128→129 |
| Stick gauche en HAUT | octet 1 · bit 0x6F · octet 2 · 128 → 129 | o1:128→127, o2:128→129 |
| Stick droit à DROITE | octet 3 · 130 → 247 · octet 4 · bit 0x7B | o3:130→247, o4:132→111, o1:128→129, o2:128→129 |
| Stick droit en HAUT | octet 4 · 130 → 9 · octet 3 · bit 0x76 | o4:130→9, o3:129→140 |
| Manette secouée | octet 3 · bit 0x79 · octet 4 · bit 0x74 | o3:128→127, o4:129→128 |

Un appui est un pic : un octet stable qui change brusquement. Une dérive de stick et un compteur de trames n'en sont pas, et n'apparaissent donc pas ici. « rien détecté » veut dire qu'aucun octet n'a sauté : la commande n'existe pas dans cette forme de rapport.
