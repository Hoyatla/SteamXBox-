# MultiPad Tester — outil de diagnostic externe

<https://github.com/nefarius/MultiPadTester> — licence MIT, même auteur que ViGEmBus et HidHide.

## À quoi il sert ici

Il affiche l'état en direct de chaque manette détectée, en parallèle, à travers six API Windows :
**XInput, Raw Input, DirectInput, HIDAPI, Windows.Gaming.Input, GameInput**. Une vue par
périphérique et par backend. Pas de remappage, pas de virtualisation : de l'inspection pure.

C'est l'instrument qui manquait. Les erreurs de diagnostic les plus coûteuses de ce projet n'ont pas
été des erreurs de code mais **d'observation** :

- une DualSense déclarée absente parce qu'un filtre cherchait `VID_054C` là où le Bluetooth écrit
  `VID&0002054C` ;
- un pad virtuel tiers lu pendant des heures à la place de la vraie manette, parce que la règle
  était « le premier emplacement XInput qui répond » ;
- un emplacement XInput occupé supposé être la manette Xbox alors qu'aucune n'était branchée.

MultiPad Tester répond directement à ces trois questions, et sans passer par notre code.

## Les questions qu'il tranche

| Question | Ce qu'on regarde |
|---|---|
| Sur quelle API cette manette apparaît-elle ? | La DualSense est en HID, jamais en XInput. Les onglets le montrent côte à côte. |
| Un pad fantôme occupe-t-il un emplacement ? | L'onglet XInput liste les emplacements réellement occupés. |
| La manette envoie-t-elle vraiment ce que nous décodons ? | Pousser un stick à fond et comparer avec la ligne `DualSense report … L=(…) R=(…)` de notre journal. |
| Un bouton est-il mal mappé ou mal lu ? | Si le bouton répond correctement ici et pas chez nous, la faute est dans notre décodage. |

## Précaution obligatoire

**Arrêter `SteamXBox.Core.exe` avant de le lancer.**

Les deux ouvrent les mêmes interfaces HID, et un périphérique ne s'ouvre pas deux fois en écriture ;
les lectures concurrentes se gênent aussi. Lancés ensemble, ils produisent un conflit qu'on prendrait
pour un défaut de la manette — exactement le genre de faux diagnostic que cet outil est censé
éliminer. C'est la même mécanique que HidHide ou le logiciel Sony : un seul propriétaire à la fois.

## Ce qu'il ne couvre pas

Ni ViGEm, ni l'attribution des emplacements XInput. Donc ni la boucle de rétroaction où le Core
relisait son propre pad virtuel, ni la question de savoir quel emplacement appartient à qui. Ces
deux-là se diagnostiquent par `steamxbox-debug.log`, qui journalise les emplacements physiques
relevés **avant** toute création de pad virtuel.

## Statut vis-à-vis du produit

Outil externe, **non redistribué** avec SteamXBox : il ne figure donc pas dans
`THIRD-PARTY-NOTICES.txt`, qui ne couvre que ce que nous distribuons. Sa licence MIT le rendrait
compatible si nous décidions un jour de l'embarquer, et l'obligation serait alors d'en conserver la
mention de copyright.
