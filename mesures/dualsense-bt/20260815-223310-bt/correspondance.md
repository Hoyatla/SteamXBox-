# Correspondance des commandes — DualSense

- Date : 2026-08-15 22:34
- Transport : **Bluetooth**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&15f755c8&3&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:90b685f7696f`
- Rapport d'entrée : 78 octets


| Commande | Ce qui bouge dans le rapport | trames |
|---|---|---|
| Croix | octet 5 · bit 0x20 | 78 |
| Rond | octet 5 · 8 → 72 | 98 |
| Carré | octet 5 · bit 0x10 | 115 |
| Triangle | octet 5 · 8 → 136 | 126 |
| L1 | octet 6 · bit 0x01 | 74 |
| R1 | octet 6 · bit 0x02 | 48 |
| L2 | octet 6 · bit 0x04 · octet 8 · 0 → 253 | 100 |
| R2 | octet 6 · bit 0x08 · octet 9 · 0 → 255 | 95 |
| L3 | octet 1 · bit 0x7E · octet 2 · bit 0x80 · octet 6 · 0 → 64 | 660 |
| R3 | octet 3 · 127 → 96 · octet 4 · bit 0x7B · octet 6 · 0 → 128 | 7728 |
| Create | octet 6 · bit 0x10 | 163 |
| Options | octet 6 · bit 0x20 | 101 |
| PS | octet 4 · bit 0x01 · octet 7 · bit 0x01 | 39 |
| Muet | passée | 0 |

« rien détecté » est une mesure : la commande n'apparaît pas dans cette forme de rapport. Sur un axe, la lecture est une course `repos → extrême` ; sur un bouton, le bit passé à 1.
