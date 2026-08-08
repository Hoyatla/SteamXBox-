namespace SteamXBox.Shell.Localization;

/// <summary>
/// French source text to English. Anything absent falls through unchanged, which is why
/// language-neutral labels (L4, ViGEmBus, numbers, Menu, View) need no entry at all.
/// </summary>
public static class Translations
{
    public static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // ---- Navigation and window ----
        ["🏠  Accueil"] = "🏠  Home",
        ["📋  Profils"] = "📋  Profiles",
        ["🎮  Xbox"] = "🎮  Xbox",
        ["⚙  Paramètres"] = "⚙  Settings",
        ["📄  Logs"] = "📄  Logs",
        ["🔧  Debug"] = "🔧  Debug",
        ["Accueil"] = "Home",
        ["Paramètres"] = "Settings",
        ["Profils"] = "Profiles",

        ["Thème Windows"] = "Windows theme",
        ["Manettes concurrentes"] = "Competing controllers",
        ["Emplacements XInput"] = "XInput slots",
        ["Autres manettes"] = "Other controllers",
        ["occupé"] = "taken",
        ["libre"] = "free",
        ["Aucune"] = "None",
        ["La manette Steam est détectée mais ne peut pas être ouverte : Windows n'a pas réussi à la démarrer. Débranchez et rebranchez-la."]
            = "The Steam Controller is detected but cannot be opened: Windows failed to start it. Unplug it and plug it back in.",
        ["Une autre manette occupe l'emplacement XInput 0, celui que les jeux lisent pour le joueur 1. La manette virtuelle de SteamXBox se retrouve après elle et n'est pas lue. Éteignez les autres manettes puis relancez le jeu."]
            = "Another controller holds XInput slot 0, the one games read for player one. SteamXBox's virtual pad ends up behind it and is never read. Turn the other controllers off, then restart the game.",
        ["Curseurs"] = "Cursors",
        ["Glissez pour sélectionner une zone. Échap pour annuler."] = "Drag to select a region. Esc to cancel.",
        ["Capture annulée."] = "Capture cancelled.",
        ["Vider"] = "Clear",
        ["Historique vidé."] = "History cleared.",
        ["Copié. Aucune fenêtre où coller : utilisez Ctrl+V."] = "Copied. No window to paste into: use Ctrl+V.",
        ["Sélectionnez une entrée pour la coller dans la fenêtre où vous étiez. L'historique n'est pas enregistré sur le disque."]
            = "Pick an entry to paste it into the window you were in. The history is never written to disk.",
        ["{0} est introuvable."] = "{0} was not found.",
        ["Capture {0}×{1} copiée et enregistrée dans {2}"] = "Capture {0}×{1} copied and saved to {2}",
        ["Capture {0}×{1} copiée dans le presse-papiers."] = "Capture {0}×{1} copied to the clipboard.",
        ["Aucun paquet de curseurs sélectionné."] = "No cursor pack selected.",
        ["Ces réglages modifient Windows, pas seulement SteamXBox. L'état d'origine est sauvegardé avant la première modification et peut être restauré à tout moment."]
            = "These settings change Windows itself, not only SteamXBox. The original state is saved before the first change and can be restored at any time.",
        ["Restaurer les réglages Windows d'origine"] = "Restore the original Windows settings",
        ["Aucun plugin de thème Windows installé."] = "No Windows theme plugin installed.",
        ["Point d'entrée du plugin introuvable."] = "Plugin entry point not found.",
        ["Réglages Windows d'origine restaurés."] = "Original Windows settings restored.",
        ["Aucune sauvegarde à restaurer."] = "No backup to restore.",
        // ---- SteamXBox Desktop ----
        // Les libelles des tuiles sont ceux de QuickActions, sans accents : ils servent de cle de
        // traduction et une cle accentuee se retrouve tot ou tard recopiee de travers.
        ["Centre de contrôle"] = "Control centre",
        ["Direction pour naviguer, Entrée pour ouvrir."] = "Arrows to move, Enter to open.",
        ["Manette : reglages et profils"] = "Controller: settings and profiles",
        ["Sortie audio"] = "Audio output",
        ["Casque, enceintes ou HDMI"] = "Headset, speakers or HDMI",
        ["Reseaux et connexion sans fil"] = "Networks and wireless",
        ["Appareils et appairage"] = "Devices and pairing",
        ["Luminosite"] = "Brightness",
        ["Affichage et luminosite"] = "Display and brightness",
        ["Ne pas deranger"] = "Do not disturb",
        ["Assistant de concentration"] = "Focus assist",
        ["Presse-papiers"] = "Clipboard",
        ["Historique des copies"] = "Copy history",
        ["Capture"] = "Snip",
        ["Capturer une zone de l'ecran"] = "Capture part of the screen",
        ["Calculatrice"] = "Calculator",
        ["Calculatrice scientifique"] = "Scientific calculator",
        ["Calculatrice scientifique de SteamXBox"] = "SteamXBox scientific calculator",
        ["Gestionnaire des taches"] = "Task manager",
        ["Processus et performances"] = "Processes and performance",
        ["Parametres SteamXBox"] = "SteamXBox settings",
        ["Paramètres SteamXBox"] = "SteamXBox settings",
        ["Preferences, journaux et diagnostic"] = "Preferences, logs and diagnostics",
        ["Ouvrir les parametres"] = "Open settings",


        // ---- Home ----
        ["Contrôleur"] = "Controller",
        ["Mode actuel"] = "Current mode",
        ["Profil actif"] = "Active profile",
        ["Démarrer automatiquement à la détection de la manette"] = "Start automatically when the controller is detected",
        ["Démarrer automatiquement au lancement"] = "Start automatically when the controller is detected",
        ["Démarrer avec Windows"] = "Start with Windows",
        ["Éteindre la manette"] = "Turn the controller off",
        ["Maintenez Menu et View ensemble pendant 3 secondes. SteamXBox débranche la manette virtuelle puis s'arrête proprement : ce n'est pas traité comme une déconnexion accidentelle."]
            = "Hold Menu and View together for 3 seconds. SteamXBox unplugs the virtual controller and exits cleanly: this is not treated as an accidental disconnection.",
        ["Les deux boutons doivent être enfoncés en même temps. Relâcher l'un des deux remet le compte à zéro."]
            = "Both buttons must be down at the same time. Releasing either one restarts the count.",

        // ---- Profiles: structure ----
        ["Sélectionnez un profil pour l'éditer"] = "Select a profile to edit it",
        ["Profil par défaut"] = "Default profile",
        ["Le profil « Default » porte les réglages de référence. Il n'est pas modifiable, mais peut être restauré."]
            = "The “Default” profile holds the reference settings. It cannot be edited, but it can be restored.",
        ["🔄  Restaurer les paramètres par défaut"] = "🔄  Restore default settings",
        ["Valeurs par défaut"] = "Default values",
        ["Sauvegarder"] = "Save",
        ["Appliquer"] = "Apply",
        ["Supprimer"] = "Delete",

        // ---- Profiles: movement ----
        ["Mouvements"] = "Movement",
        ["Mouvements :"] = "Movement:",
        ["GAUCHE"] = "LEFT",
        ["DROITE"] = "RIGHT",
        ["Pad gauche"] = "Left pad",
        ["Pad droit"] = "Right pad",
        ["Sensibilité pad"] = "Pad sensitivity",
        ["Dead zone pad"] = "Pad dead zone",
        ["Inversion pad"] = "Pad inversion",
        ["Stick gauche"] = "Left stick",
        ["Stick droit"] = "Right stick",
        ["Stick droite"] = "Right stick",
        ["Souris"] = "Mouse",
        ["Dead zone stick"] = "Stick dead zone",
        ["Dead Zone sticks"] = "Stick dead zones",
        ["Sensibilité sticks"] = "Stick sensitivity",
        ["Inversion Y"] = "Invert Y",
        ["Aucun"] = "None",

        // ---- Profiles: behaviour ----
        ["Comportement"] = "Behaviour",
        ["Comportement :"] = "Behaviour:",
        ["Accélération"] = "Acceleration",
        ["Inertie"] = "Inertia",
        ["Scroll horizontal"] = "Horizontal scroll",
        ["Précision fine"] = "Fine precision",
        ["Continuation en bord"] = "Edge continuation",
        ["Seuil de lancer"] = "Throw threshold",
        ["Force vibration"] = "Vibration strength",
        ["Fréquence vibration"] = "Vibration rate",
        ["Activer"] = "Enable",
        ["Précision fine : force de la réduction sur un petit geste. Portée précision : distance sur laquelle elle s'applique avant de revenir à la normale. Seuil de lancer : en dessous, relâcher ne projette pas le curseur. Anti-frôlement : distance qu'un nouveau contact doit parcourir avant d'agir."]
            = "Fine precision: how much a small gesture is scaled down. Throw threshold: below this distance, releasing does not fling the pointer.",

        // ---- Overlay keyboard ----
        ["Overlay Keyboard"] = "Overlay Keyboard",
        ["Ces réglages s'appliquent au clavier virtuel, pas au bureau Windows."]
            = "These settings apply to the on-screen keyboard, not to the Windows desktop.",
        ["Mode de saisie"] = "Typing mode",
        ["Taille du clavier"] = "Keyboard size",
        ["Clavier flottant (sinon fixe en bas de l'écran)"] = "Floating keyboard (otherwise pinned to the bottom of the screen)",
        ["Intensité vibrations"] = "Vibration intensity",
        ["Force clic pad gauche"] = "Left pad click strength",
        ["Force clic pad droit"] = "Right pad click strength",
        ["Vibrer au survol des touches"] = "Vibrate when passing over keys",
        ["Un tic à chaque touche franchie, pour taper sans regarder l'overlay."]
            = "A tick on every key boundary crossed, so you can type without watching the overlay.",
        ["Valider au relâchement du clic"] = "Commit on click release",
        ["Permet de repositionner le doigt en maintenant le clic avant de valider."]
            = "Lets you reposition your finger while holding the click before committing.",
        ["Clavier complet"] = "Full keyboard",

        // ---- Xbox tab ----
        ["Mode Xbox360"] = "Xbox360 mode",
        ["Pass-through"] = "Pass-through",
        ["Lorsque le mode Xbox est actif, le contrôleur Steam est transmis tel quel au jeu via le contrôleur virtuel Xbox 360."]
            = "While Xbox mode is active, the Steam Controller is passed through to the game as a virtual Xbox 360 controller.",
        ["Les paramètres ci-dessous contrôlent le comportement de la manette dans ce mode."]
            = "The settings below control how the controller behaves in this mode.",
        ["Utilisez le bouton quick-access pour basculer entre les modes."]
            = "Use the quick-access button to switch between modes.",
        ["Sticks &amp; Triggers"] = "Sticks &amp; Triggers",
        ["Seuil triggers"] = "Trigger threshold",
        ["Vibration"] = "Vibration",
        ["Activer la vibration"] = "Enable vibration",
        ["Intensité motricité"] = "Motor intensity",
        ["Transfert haptique"] = "Haptic forwarding",
        ["Bouton Steam"] = "Steam button",
        ["Bouton Quick Access"] = "Quick Access button",
        ["Guide Xbox (défaut)"] = "Xbox Guide (default)",
        ["Activé (défaut)"] = "Enabled (default)",
        ["Désactivé"] = "Disabled",
        ["Bumper L4/R4"] = "Bumper L4/R4",
        ["Bumper L5/R5"] = "Bumper L5/R5",
        ["Boutons"] = "Buttons",
        ["Dead zone sticks"] = "Stick dead zone",
        ["Courbe sticks"] = "Stick curve",
        ["Seuil gâchettes"] = "Trigger threshold",
        ["Point de fond"] = "Full-press point",
        ["Intensité vibration"] = "Vibration intensity",
        ["Transférer aussi aux pads"] = "Also send to the trackpads",
        ["Haptique des gâchettes (expérimental)"] = "Trigger haptics (experimental)",
        ["Envoyer la vibration aux gâchettes"] = "Send vibration to the triggers",
        ["Force gâchettes"] = "Trigger strength",
        ["Index actionneur"] = "Actuator index",
        ["Courbe : 50 % est linéaire. En dessous, la visée fine gagne en précision ; au-dessus, la pleine amplitude arrive plus tôt."]
            = "Curve: 50% is linear. Below that, fine aim gains precision; above it, full deflection arrives sooner.",
        ["Sous le seuil, la gâchette ne renvoie rien. Au point de fond, elle renvoie le maximum : le descendre raccourcit la course."]
            = "Below the threshold a trigger reports nothing. At the full-press point it reports maximum: lowering it shortens the throw.",
        ["Aucun actionneur de gâchette n'est confirmé sur ce firmware. Lancez « SteamXBox.Core.exe haptic-probe » pour le savoir, puis renseignez l'index trouvé."]
            = "No trigger actuator has been confirmed on this firmware. Run “SteamXBox.Core.exe haptic-probe” to find out, then enter the index it reports.",
        ["Profil"] = "Profile",
        ["Nouveau"] = "New",
        ["Les boutons Steam et Quick Access ne sont pas remappables : ils pilotent SteamXBox lui-même."]
            = "The Steam and Quick Access buttons cannot be remapped: they drive SteamXBox itself.",
        ["🔄  Restaurer le mapping par défaut"] = "🔄  Restore the default mapping",
        ["Mapping remis aux valeurs par défaut."] = "Mapping restored to its default values.",
        ["Erreur d'enregistrement : {0}"] = "Could not save: {0}",
        ["Boutons :"] = "Buttons:",
        ["L3 (stick)"] = "L3 (stick)",
        ["R3 (stick)"] = "R3 (stick)",

        // ---- Settings ----
        ["Pilotes"] = "Drivers",
        ["Recharger"] = "Reload",
        ["Télécharger"] = "Download",
        ["Minimiser dans la barre des tâches"] = "Minimise to the system tray",
        ["Intervalle de détection device"] = "Device detection interval",
        ["Langue"] = "Language",
        ["Thème"] = "Theme",
        ["Thème intégré"] = "Built-in theme",
        ["L'apparence par défaut de SteamXBox."] = "The default SteamXBox look.",
        ["{0} — appliqué au prochain démarrage."] = "{0} — applied at the next start.",
        ["Suivre Windows"] = "Follow Windows",
        ["Français"] = "French",
        ["Anglais"] = "English",
        ["Rejoindre le Discord"] = "Join the Discord",
        ["À propos"] = "About",
        ["Steam Controller → Xbox 360 Virtual Gamepad Bridge"] = "Steam Controller → Xbox 360 Virtual Gamepad Bridge",

        // ---- Etiquettes courtes de la coque et des cartes ----
        // Beaucoup d'etiquettes de cette interface sont deja neutres — L4, RB, Menu, View, Xbox,
        // ViGEmBus, DPad — et n'ont donc aucune entree ici : elles traversent le dictionnaire telles
        // quelles. Seules celles qui sont reellement des mots francais, ou des mots anglais qu'un
        // francophone lirait mal, sont listees.
        ["Status"] = "Status",
        ["Mode"] = "Mode",
        ["Log"] = "Log",
        ["Log:"] = "Log:",
        ["Core:"] = "Core:",
        ["|  Device:"] = "|  Device:",
        ["Support Me, Plz!"] = "Support Me, Plz!",

        // ---- Resume du profil par defaut ----
        // Les fleches et les noms de touches sont neutres ; seul "Aucun" doit changer de langue.
        ["L3 → Enter          R3 → Aucun"] = "L3 → Enter          R3 → None",
        // ---- Log and debug ----
        ["Effacer"] = "Clear",
        ["Dernières lignes du log"] = "Last log lines",
        ["Informations système"] = "System information",
        ["Rapport de diagnostic"] = "Diagnostic report",
        ["Générez un rapport complet contenant les logs, l'état des pilotes et les informations système. Utile pour le dépannage."]
            = "Generate a full report with the logs, driver status and system information. Useful for troubleshooting.",
        ["📋  Copier le rapport"] = "📋  Copy report",
        ["💾  Sauvegarder le rapport"] = "💾  Save report",
        ["📂  Ouvrir le dossier logs"] = "📂  Open logs folder",
        ["Version"] = "Version",
        [".NET Runtime"] = ".NET Runtime",

        // ---- Status labels and messages produced in code ----
        // These reach the UI through Strings.Current[...] rather than a XAML binding, so a language
        // change only repaints them the next time they are recomputed.
        ["Arrêté"] = "Stopped",
        ["En cours"] = "Running",
        ["En cours ({0})"] = "Running ({0})",
        ["Erreur au démarrage"] = "Failed to start",
        ["Inconnu"] = "Unknown",
        ["Erreur"] = "Error",
        ["Installé"] = "Installed",
        ["Non installé"] = "Not installed",
        ["Aucun device"] = "No device",
        ["Désactivées"] = "Off",
        ["Aucun fichier de log trouvé."] = "No log file found.",
        ["Erreur lecture log : {0}"] = "Error reading the log: {0}",
        ["Impossible de modifier le démarrage Windows."] = "Could not change the Windows startup entry.",
        ["Windows lancera : {0}"] = "Windows will launch: {0}",
        ["Windows lancera une autre copie : {0}"] = "Windows will launch a different copy: {0}",
        ["Profil sauvegardé."] = "Profile saved.",
        ["Paramètres appliqués."] = "Settings applied.",
        ["Nouveau profil créé à partir de la configuration actuelle."] = "New profile created from the current configuration.",
        ["Impossible de créer un profil nommé 'Default'."] = "A profile cannot be named “Default”.",
        ["Profil « Default » restauré aux valeurs d'usine."] = "The “Default” profile has been restored.",
        ["Profil « {0} » sauvegardé automatiquement."] = "Profile “{0}” saved automatically.",
        ["Sélectionnez une manette pour enregistrer ce profil."] = "Select a controller to save this profile.",
        ["Sélectionnez une manette pour supprimer son profil."] = "Select a controller to delete its profile.",
        ["Profil « {0} » associé à {1}."] = "Profile “{0}” attached to {1}.",
        ["Réglages appliqués à {0}."] = "Settings applied to {0}.",
        ["La manette « {0} » n'a pas de profil."] = "Controller “{0}” has no profile.",
        ["Profil de « {0} » supprimé. Elle revient aux réglages par défaut."] = "Profile of “{0}” deleted. It reverts to the default settings.",
        ["Sélectionnez une manette en haut pour l'associer à ce profil."] = "Select a controller at the top to attach this profile to it.",
        ["Aucun profil — il sera créé à la sauvegarde."] = "No profile yet — it will be created when you save.",
        ["Profil « {0} » — il sera écrasé à la sauvegarde."] = "Profile “{0}” — it will be overwritten when you save.",
        ["Modifier ce profil crée ou écrase le profil de la manette sélectionnée ; « Default » n'est jamais modifié."] = "Editing creates or overwrites the selected controller's profile; “Default” itself is never modified.",
        ["Renommez la manette : « Default » est réservé au profil de référence."] = "Rename the controller: “Default” is reserved for the reference profile.",
        ["PROFIL DE LA MANETTE"] = "CONTROLLER PROFILE",

        // ---- Log lines ----
        ["[{0}] [INFO] Core arrêté (code {1})\n"] = "[{0}] [INFO] Core stopped (code {1})\n",
        ["[{0}] [INFO] Profil '{1}' écrit sur disque avant démarrage\n"]
            = "[{0}] [INFO] Profile '{1}' written to disk before starting\n",
        ["[{0}] [ERROR] Impossible d'écrire le profil '{1}' : {2}\n"]
            = "[{0}] [ERROR] Could not write profile '{1}': {2}\n",
        ["[{0}] [INFO] Démarrage Core : {1} (exists={2})\n"] = "[{0}] [INFO] Starting core: {1} (exists={2})\n",
        ["[{0}] [INFO] Profil actif : {1} ({2})\n"] = "[{0}] [INFO] Active profile: {1} ({2})\n",
        ["[{0}] [ERROR] Échec du démarrage de Core\n"] = "[{0}] [ERROR] Core failed to start\n",

        // ---- Default-profile summary ----
        ["Pad gauche → Scroll (4.8 crans/unité, dead zone 0.002)"]
            = "Left pad → Scroll (4.8 notches/unit, dead zone 0.002)",
        ["Pad droit → Trackball (380 px/unité, dead zone 0.00015)"]
            = "Right pad → Trackball (380 px/unit, dead zone 0.00015)",
        ["Stick gauche → Flèches directionnelles"] = "Left stick → Arrow keys",
        ["Inversion : pad gauche Y, pad droit Y"] = "Inversion: left pad Y, right pad Y",
        ["Dead zone stick gauche : 0.06    stick droite : 0.018"]
            = "Left stick dead zone: 0.06    right stick: 0.018",
        ["Accélération : gauche 1.5, droite 2.0"] = "Acceleration: left 1.5, right 2.0",
        ["Inertie : gauche et droite 2.0 (glisse longue)"] = "Inertia: left and right 2.0 (long glide)",
        ["Précision fine : 0.10    Seuil de lancer : 70 px"]
            = "Fine precision: 0.10    Throw threshold: 70 px",
        ["Continuation en bord : 750 px/s"] = "Edge continuation: 750 px/s",
    };
}
