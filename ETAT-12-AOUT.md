# État au 12 août — à lire avant de reprendre

Document de reprise. Il existe pour que la prochaine séance ne recommence pas les mesures de
celle-ci. Écrit après une journée au bilan médiocre : deux régressions introduites puis corrigées,
et une seule cause réelle identifiée tard.

---

## 1. La question ouverte, et le test qui la tranche

**En mode Xbox, la manette Steam envoie les boutons et rien d'autre — à confirmer.**

Mesuré le 12 août à 11:27, quatre secondes en mode Xbox :

```
xbox buttons=91  sticks=0  triggers=0  submitFail=0  pressed=A
```

Les boutons partent vers la manette virtuelle, rien n'est refusé. Les sticks et les gâchettes sont
à zéro — **mais la ligne dit `pressed=A`**, donc seuls des boutons étaient actionnés. Un stick qu'on
ne touche pas compte zéro, légitimement. *Cette mesure ne prouve donc rien.*

### Le test, dix secondes

En mode Xbox, isolément :

1. bouger **seulement le stick gauche**, 3 s
2. puis **seulement les gâchettes**, 3 s

Puis lire `SenSÉ-debug.log`, lignes `mode=Xbox360`.

| Résultat | Conclusion |
|---|---|
| `sticks=0 triggers=0` en les actionnant | cassé — chercher dans `TritonInputReportParser` (sticks aux octets 9/11 et 13/15, gâchettes 5/7) puis dans `Tuning.ApplyStick` (zone morte) |
| `sticks=N triggers=N` | le chemin fonctionne, le défaut est ailleurs |

### Ce qui est déjà vérifié dans le code, inutile d'y retourner

- `XboxButtonMap.Common` contient ABXY, bumpers, clics de sticks (`LeftStick→LeftThumb`,
  `RightStick→RightThumb`), croix directionnelle, et les palettes.
- `DefaultSteamControllerMapper.Map` mappe bien les deux gâchettes et les deux sticks.
- `TritonInputReportParser` remplit bien sticks et gâchettes.

Donc le mappage est **écrit**. S'il ne sort rien, la cause est dans les valeurs qui entrent ou dans
la zone morte, pas dans la table.

---

## 2. Divergence à trancher : les palettes arrière

| | L4 | R4 | L5 | R5 |
|---|---|---|---|---|
| **Code actuel** | X | Y | A | B |
| **Demandé le 12 août** | A | B | B | Y |

La liste demandée contient `B` deux fois et pas de `X` — coquille probable. **Non corrigé**
volontairement : une inversion faite au jugé le matin même (Menu/View sur la DualSense) avait cassé
un mapping correct. À trancher avec les quatre palettes dans l'ordre.

---

## 3. Les trackpads ne sont pas routés en mode Xbox

Vérifié dans le code. `DefaultSteamControllerMapper.Map` construit le rapport Xbox à partir de
`state.LeftStick`, `state.RightStick` et des deux gâchettes uniquement. Les pads n'alimentent que
`mouse`, qui ne sert qu'en mode Profil.

`PadMotionMode` ne connaît que `Trackball`, `Scroll`, `None` — trois notions de pointeur. **Il
n'existe aucun mode « pad = stick ».** La capacité n'a jamais été construite ; ce n'est pas une
régression.

Conséquence : une manette Steam n'ayant pas de stick droit physique, `RightThumb` reste à zéro en
permanence. Une modification qui routait le pad droit vers le stick droit a été écrite puis
**annulée** faute d'être demandée.

---

## 4. Ce qui a été corrigé aujourd'hui et qui tient

| Correction | Preuve |
|---|---|
| **Compteurs Xbox jamais affichés** | La condition testait le mode de la manette de la *dernière trame* au lieu du mode de la ligne. Avec plusieurs manettes, le seul chemin utile du produit était invisible. Corrigé. |
| **`SendInput` : retour jeté** | Douze appels d'`InputHelper` ignoraient la valeur de retour. Le compteur affichait `mouse events=103` pour une seconde où rien n'était parti. Passent maintenant par un point unique : `INJECTION REFUSEE ×N (erreur N)`. |
| **Élévation** | Les quatre exécutables en `requireAdministrator`, vérifié dans la ressource `RT_MANIFEST` de chaque binaire. Sans elle, aucune injection vers un lanceur de jeu. |
| **Manette Xbox physique disparue** | Ma régression. Une vraie Xbox 360 filaire porte le même `VID_045E&PID_028E` qu'une manette ViGEm ; le filtre ajouté rejetait le slot avec la vraie manette dedans. Les manettes émulées sont maintenant écartées **avant** l'association, dans `XusbInterfacePaths`. |
| **Adoption de sa propre sortie** | SenSÉ redécouvrait sa manette virtuelle 0,4 s après l'avoir créée, lui donnait un clavier, et la masquait avec HidHide. Le garde-fou existant n'avait jamais pu fonctionner (deux raisons distinctes, documentées dans `XInputDurableIdentity`). |
| **Profil non rendu après l'OSK** | `OskActive` restait vrai indéfiniment. L'incrustation bat désormais un fichier tant qu'elle est à l'écran ; une prise de main non confirmée pendant 6 s est rendue. |
| **29 périphériques fantômes** | Retirés. Un registre (`%LOCALAPPDATA%\SenSÉ\virtual-pads.txt`) note ce que le produit crée, et le désinstalleur appelle `pads-cleanup`. |

---

## 5. Ce qui est éteint et pourquoi

**Mode Bluetooth complet de la DualSense** — `RequestFullBluetoothReports = false` dans
`DualSenseControllerSource`. La bascule fonctionne (rapports `0x31`, gâchettes analogiques, pavé,
gyro) mais fait passer la cadence de 60 à 175 trames/s en moyenne, pointes à 554, et une latence
sévère a été signalée dans l'heure. Ce n'est **pas** une file qui déborde : elle est bornée et
toutes les trames sont consommées. Le coût est ailleurs et n'est pas compris. À rallumer une fois
mesuré, pas avant.

---

## 6. Travail `uiAccess`, en place et non testé

- Certificat auto-signé `CN=SenSÉ Test Signing (NE PAS LIVRER)`, empreinte
  `ECF2527F921410AC28C86D66B1BAA4D83923E3AD`, dans *Racines de confiance* et *Éditeurs approuvés*
- Copie signée du noyau dans `C:\Program Files\SenSÉTest\` — jamais exécutée
- Les deux projets injecteurs ont deux manifestes, choisis par la présence d'un certificat

`uiAccess` donnerait le droit d'injecter vers des fenêtres élevées **sans** invite UAC — ce qui
compte pour des écoles et des entreprises. Il exige signature *et* emplacement sûr.

**Pour tout retirer :**

```
certutil -delstore Root ECF2527F921410AC28C86D66B1BAA4D83923E3AD
certutil -delstore TrustedPublisher ECF2527F921410AC28C86D66B1BAA4D83923E3AD
```
puis supprimer `C:\Program Files\SenSÉTest`.

---

## 7. Pièges rencontrés, à ne pas repayer

- **Un compteur qui dit « j'ai appelé la fonction » n'est pas un compteur qui dit « c'est parti ».**
  C'est ce qui a envoyé la recherche ailleurs pendant des heures.
- **Le journal de l'incrustation horodatait en UTC**, tout le reste en heure locale. Deux heures
  d'écart ont fait écarter la bonne hypothèse une journée entière. Corrigé.
- **Vérifier le binaire livré, pas celui qu'on vient de construire.** Une recherche de texte dans
  l'exécutable ment : les runtimes .NET embarqués contiennent leurs propres manifestes. Lire la
  ressource `RT_MANIFEST`.
- **PowerShell 5.1 lit un `.ps1` sans BOM en codepage ANSI** — un caractère accentué casse le
  script, qui ne démarre pas du tout.
- **Ne jamais publier dans la racine du produit** : `dotnet publish -o <racine>` casse la
  compilation (`CS5001`). Publier à côté, puis copier.
- **L'antivirus verrouille durablement un mono-fichier rogné.** `PublishTrimmed` rend l'exécutable
  impossible à copier ou lancer sur cette machine.

---

## 8. Fichiers touchés le 12 août

Instrumentation : `RuntimeCounters.cs`, `tools/Moniteur/Program.cs`, `InputHelper.cs`
Correctifs : `XInputDurableIdentity.cs`, `DeviceTree.cs`, `ProfileMapper.cs`, `OskInstanceNaming.cs`,
`Osk/Program.cs`, `VirtualPadSet.cs`, `DualSenseControllerSource.cs`, `App.Console/Program.cs`
Élévation : les quatre `app.manifest`, `SenSÉ.Desktop.csproj`
Nouveaux : `OskPresence.cs`, `app.uiaccess.manifest`, `SenSÉ.Debug.exe`, `tools/LanceurDebug/`
Installeurs : les deux `.iss` (appel de `pads-cleanup` à la désinstallation)

`git diff` par fichier permet d'annuler sélectivement. Rien n'est à moitié fait : tout ce qui est
publié compile, 911 tests passent, et les binaires installés sont ceux qui ont été vérifiés.
