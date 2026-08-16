# Notes de séance — passe A (séance abandonnée)

Séance conduite depuis une console distante, où l'opérateur ne voyait pas les consignes. Abandonnée
au profit d'une séance relancée depuis `Banc-DualSense.exe`, en local.

| Manœuvre | Verdict |
|---|---|
| `01-repos` | **Bonne.** 12 046 trames en 20 s, 602 tr/s, aucun trou. Repos des axes : octet 1 (LX) 127-130, octet 2 (LY) 123-128, octet 3 (RX) 127-128, octet 4 (RY) 128-129. |
| `02-stick-gauche-tenu` | **À jeter.** L'octet 1 n'a jamais quitté 128-129 : le geste n'est pas tombé dans la fenêtre de mesure. Ne rien conclure de cette section. |

Ce que la manœuvre 01 a déjà donné, et qui reste valable :

- L'axe Y du stick gauche repose jusqu'à **123**, soit cinq crans sous le centre, alors que le
  `RestBand` de `DualSenseReportParser` n'en absorbe que quatre. Manette posée sur la table, le
  décodeur sort donc **0,04 en Y**. Candidat direct pour « le pointeur bouge tout seul ».
- Le débit est de **600 tr/s au repos**, sans aucun trou au-delà de 20 ms. À confronter au
  « n'émet qu'au changement » du ChangeLog 4.8, qui décrit un tout autre régime.
