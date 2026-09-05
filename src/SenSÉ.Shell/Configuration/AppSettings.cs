using Localization = SenSÉ.Shell.Localization;
using System.Text.Json.Serialization;

namespace SenSÉ.Shell.Configuration;

public sealed partial class AppSettings
{
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;

    [JsonPropertyName("minimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Démarrer le générateur d'images dès le lancement, pour qu'il soit prêt au premier clic.
    /// </summary>
    /// <remarks>
    /// <b>Éteint par défaut, et ce n'est pas de la prudence.</b> Le générateur est le plus gros
    /// consommateur du produit ; l'allumer sans qu'on l'ait demandé, c'est un processus Python et
    /// sa mémoire dès le démarrage, sur une machine qui n'ouvrira peut-être jamais cet outil de la
    /// journée. Le registre des ressources existe précisément pour que rien ne tourne sans raison.
    ///
    /// <para>
    /// Ce que ce réglage achète : le produit vit sur un disque externe, et le premier démarrage du
    /// générateur d'une session coûte deux minutes — le temps d'ouvrir un par un les
    /// soixante-douze mille fichiers de Python. Les suivants coûtent quinze secondes, Windows les
    /// gardant en mémoire. Allumé, ces deux minutes se paient pendant qu'on fait autre chose.
    /// </para>
    /// </remarks>
    [JsonPropertyName("prechaufferGenerateur")]
    public bool PrechaufferGenerateur { get; set; }

    /// <summary>Le port sur lequel le générateur d'images écoute.</summary>
    /// <remarks>
    /// <b>Un réglage, pas une constante.</b> Il était écrit en dur à quatre endroits du code — le
    /// serveur, le lecteur de flux, les options vivantes, les capacités de l'assistant. Quatre
    /// copies d'un même nombre dont trois seraient oubliées le jour où il change, et il change dès
    /// qu'on emploie une autre installation de ComfyUI que celle que le produit démarre lui-même.
    /// </remarks>
    [JsonPropertyName("generateurPort")]
    public int GenerateurPort { get; set; } = 8188;

    /// <summary>
    /// Le générateur est une installation à part, que le produit ne démarre ni n'arrête.
    /// </summary>
    /// <remarks>
    /// Pour ComfyUI Desktop, ou toute installation que l'utilisateur gère lui-même. Le produit s'y
    /// connecte et rien de plus : il ne la lance pas — ce serait en ouvrir une seconde sur un port
    /// déjà pris — et surtout il ne la tue pas à sa fermeture, parce que fermer la fenêtre de
    /// quelqu'un d'autre n'est pas du ménage.
    /// </remarks>
    [JsonPropertyName("generateurExterne")]
    public bool GenerateurExterne { get; set; }

    [JsonPropertyName("devicePollInterval")]
    public int DevicePollIntervalMs { get; set; } = 3000;

    [JsonPropertyName("lastActiveProfile")]
    public string LastActiveProfile { get; set; } = "Default";

    /// <summary>Folder name of the selected theme under Themes, or empty for the built-in look.</summary>
    public string Theme { get; set; } = "";

    /// <summary>Interface language. Defaults to following the Windows display language.</summary>
    [JsonPropertyName("language")]
    public Localization.AppLanguage Language { get; set; } = Localization.AppLanguage.System;

    /// <summary>
    /// Launch SenSÉ when Windows starts, via the per-user Run key. Distinct from
    /// <see cref="AutoStart"/>, which only starts the core once the controller is detected.
    /// </summary>
    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Window size remembered per tab, indexed by tab order. Size only: the window position is left
    /// alone so it never reappears off-screen or jumps between monitors.
    /// </summary>
    [JsonPropertyName("tabSizes")]
    public TabSize[] TabSizes { get; set; } = [];

    public static string SettingsDirectory =>
        System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SenSÉ");

    public static string SettingsFilePath =>
        System.IO.Path.Combine(SettingsDirectory, "settings.json");
}

/// <summary>Remembered window size for one tab.</summary>
public sealed class TabSize
{
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonIgnore]
    public bool IsUsable => Width > 200 && Height > 200;
}
