# Correspondance des commandes

- Date : 2026-08-15 22:48
- Transport : **Bluetooth**
- Chemin : `\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#8&15f755c8&3&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}`
- Clé durable : `bt:90b685f7696f`
- Rapport d'entrée : 78 octets

| Commande | Ce qui bouge dans le rapport | tout ce qui a bougé |
|---|---|---|
| Croix | octet 7 · bit 0x30 · octet 5 · bit 0x20 | o7:12→60, o5:8→40, o1:128→127 |
| Rond | octet 5 · 8 → 72 · octet 7 · bit 0x10 | o5:8→72, o7:44→0, o1:128→127 |
