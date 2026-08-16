# Passe A — dépouillement

Séance du 15 août 2026, 20:07. DualSense en Bluetooth, rapport `0x01` sur 78 octets, clé durable
`bt:44464836686d`. Treize manœuvres, tous les gestes comptés, aucun plafond. 172 000 trames.

Tout ce qui suit est mesuré. Les conclusions tirées d'un seul geste sont signalées comme telles.

---

## 1. La carte du rapport de compatibilité

| Octet | Contenu | Preuve |
|---|---|---|
| 0 | identifiant `0x01` | toutes les manœuvres |
| 1 | **stick gauche X**, repos 127-129 | 03, 04 : parcourt 0x00-0xFF |
| 2 | **stick gauche Y**, repos 126-128 | 03, 04 : parcourt 0x00-0xFF |
| 3 | **stick droit X**, repos 126-127 | 05 : parcourt 0x03-0xF8 |
| 4 | **stick droit Y**, repos 126-128 | 05 : parcourt 0x00-0xFF |
| 5 | bits 0-3 **croix directionnelle** (hat 0-8), bits 4-7 **boutons de face** | 07 : masque `0xF0` ; 08 : masque `0x0E` |
| 6 | `0x01` L1 · `0x02` R1 · `0x04` **L2** · `0x08` **R2** · `0x10` Create · `0x20` Options · `0x40` L3 · `0x80` R3 | 06, 09, 10, chacun isolé dans le temps |
| 7 | bits 2-5 **compteur de trames** (pas de 4) · `0x02` **contact du pavé tactile** · `0x01` **bouton système** | 10 et 11 |
| 8 | **L2**, uniquement `0x00` ou `0xFF` | 06 : deux valeurs sur une course lente |
| 9 | **R2**, uniquement `0x00`, `0xF6` ou `0xFF` | 06 : trois valeurs sur une course lente |
| 10-77 | **constants sur toute la séance** | treize manœuvres, aucune variation |

Les soixante-huit derniers octets ne portent rien. Le rapport de compatibilité tient en dix octets.

---

## 2. Ce que la mesure confirme

**Le mappage écrit dans `DualSenseReportParser` est juste.** Chaque bit a été vu bouger seul, au
moment où le bouton correspondant était pressé :

- boutons de face : `0x10` carré, `0x20` croix, `0x40` rond, `0x80` triangle — le décodage sort
  X, A, B, Y aux bons instants ;
- épaules : `0x01` L1, `0x02` R1, `0x40` L3, `0x80` R3 ;
- **Create à gauche = `0x10` = View, Options à droite = `0x20` = Menu.** La règle positionnelle
  tient, mesurée. Rien à inverser, et cette ligne clôt la question rouverte le 12 août.

**La croix directionnelle est bien un hat**, valeurs 0, 2, 4, 6 et 8 au centre. Les diagonales
n'ont jamais été émises malgré deux tentatives — le bit 0 de l'octet 5 n'a pas bougé une fois en
28 secondes. Le décodage des valeurs 1, 3, 5, 7 reste donc non prouvé, mais le modèle est bon.

---

## 3. Ce que la mesure réfute

### La manette ne se tait jamais

**600 trames par seconde, en permanence, quoi qu'on fasse.** Manette posée sur la table : 599 tr/s.
Stick poussé contre sa butée et tenu immobile douze secondes : 591 tr/s. Intervalle p99 de 10 ms,
maximum réel de 20 ms, aucun trou.

L'hypothèse qui a orienté toute cette séance — *le pointeur saccade parce que la manette se tait
quand le stick ne bouge plus* — est **fausse sur ce transport et cette machine**. Le pointeur
saccadé de la DualSense a une autre cause, en aval de la lecture HID.

Le compteur de l'octet 7 avance à chaque trame : ce sont bien 600 rapports produits par la manette,
pas une trame rejouée par Windows.

> Cela contredit frontalement le ChangeLog 4.8, qui décrit « 8 à 541 images par seconde » et un pad
> qui « n'émet qu'au changement ». Les deux mesures ne peuvent pas décrire la même chose : à
> rapprocher du lien Bluetooth utilisé ce jour-là (dongle contre Bluetooth intégré), qui est la
> seule variable qui a changé.

### Les gâchettes ne sont pas analogiques

Une course lente de zéro à fond produit **deux valeurs** sur l'octet 8, **trois** sur l'octet 9. Pas
de progression, pas d'intermédiaire. La documentation interne avait raison, le placement des octets
dans le décodeur aussi : les deux étaient compatibles, la contradiction n'était qu'apparente. Les
gâchettes analogiques exigent le mode complet `0x31`.

### Ni gyroscope, ni accéléromètre, ni coordonnées de pavé

Manette secouée dans l'espace sans toucher aucune commande : seuls le bruit des sticks et le
compteur bougent. Doigt glissé sur le pavé : **un seul bit change**, aucune coordonnée. En
compatibilité, le pavé tactile est un contacteur, pas une surface.

---

## 4. Ce qui reste ouvert

**Le bit `0x01` de l'octet 7 est un vrai bouton système** — il ne bouge dans aucune des onze autres
manœuvres, et il apparaît dans la 10. Mais la manœuvre 10 a duré 78 secondes d'essais répétés, et
elle ne permet pas de dire **lequel** des trois — PS, Muet, ou clic du pavé — le produit. Le bit
`0x02`, lui, est établi : il apparaît aussi dans la manœuvre 11, où seul le pavé est touché.

C'est la seule question qui empêche encore de rendre à la DualSense un bouton de changement de mode.
Elle se tranche en quarante secondes avec la manœuvre `14` ajoutée au protocole : deux PS, pause,
deux Muet, pause, deux clics de pavé, et l'ordre des épisodes dans le temps donne la réponse.

**Rien ne sera recâblé avant cette trace** — c'est ce qui a déjà coûté une manette qui changeait de
mode toute seule deux fois par seconde.

---

## 5. Un artefact du banc, à ne pas lire comme une mesure

Chaque manœuvre à gestes porte **un trou de 120 à 160 ms, toujours à t = 0,73 s**. Ce n'est pas la
manette : c'est le bip de départ du banc, `Console.Beep` bloquant 120 ms dans la boucle de lecture,
juste après le relevé de repos. Les deux manœuvres de repos, dont le bip tombe hors de
l'enregistrement, ont un maximum de 17 et 21 ms.

Corrigé depuis : le bip part sur un fil séparé. Les séances à venir n'auront plus ce trou. Aucune
autre conclusion n'en dépend — le trou est unique, toujours au même instant, et hors de la fenêtre
des gestes.

---

## 6. Ce que ça change dans le code

| Où | Quoi | Fondé sur |
|---|---|---|
| `DualSenseReportParser.RestBand` | 4/128 est **trop serré**. La dérive au repos atteint 5 crans (mesuré 123 sur l'axe Y gauche à 19:47), et le décodeur sort alors 0,04 sur une manette posée. Porter à 6/128, ou mieux, relever le repos à l'attache. | manœuvres 01 et 13 |
| `DualSenseReportParser` octet 6 | Les bits `0x04` et `0x08` — L2 et R2 en numérique — ne sont pas lus. Redondants avec les octets 8/9, mais ils sont la seule source de gâchette si la forme du rapport change. | manœuvre 06 |
| `DualSenseReportParser` octet 7 | Le commentaire dit « cet octet n'est un bouton nulle part dans ce rapport ». C'est vrai des bits hauts, **faux des deux bits bas** : `0x02` est le contact du pavé, `0x01` un bouton système. Le masque du compteur est `0x3C`, pas l'octet entier. | manœuvres 10 et 11 |
| `Ps5ControllerDefaults` | Les gâchettes sont tout-ou-rien sur ce transport : tout réglage de seuil analogique est sans effet en Bluetooth compat. | manœuvre 06 |
| `DualSenseControllerSource` | Le commentaire de `AskForFullReports` décrit un pad qui n'émet qu'au changement, à cadence variable. Sur cette machine il émet à 600 Hz constants. À reformuler ou à qualifier par le lien Bluetooth. | toutes |
| Enquête pointeur DualSense | À rouvrir ailleurs que dans la source HID. La manette livre 600 trames/s sans trou ; ce qui saccade est en aval. | manœuvre 02 |
