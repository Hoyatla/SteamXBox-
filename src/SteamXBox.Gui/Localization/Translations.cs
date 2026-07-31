namespace SteamXBox.Gui.Localization;

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

        // ---- Home ----
        ["Contrôleur"] = "Controller",
        ["Mode actuel"] = "Current mode",
        ["Profil actif"] = "Active profile",
        ["Démarrer automatiquement à la détection de la manette"] = "Start automatically when the controller is detected",
        ["Démarrer automatiquement au lancement"] = "Start automatically when the controller is detected",
        ["Démarrer avec Windows"] = "Start with Windows",

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
        ["Suivre Windows"] = "Follow Windows",
        ["Français"] = "French",
        ["Anglais"] = "English",
        ["À propos"] = "About",
        ["Steam Controller → Xbox 360 Virtual Gamepad Bridge"] = "Steam Controller → Xbox 360 Virtual Gamepad Bridge",

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
