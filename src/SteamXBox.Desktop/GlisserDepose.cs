using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SteamXBox.Desktop;

/// <summary>
/// Rouvre le glisser-déposer vers une fenêtre du produit.
/// </summary>
/// <remarks>
/// <b>Le code du dépôt n'a jamais été en cause.</b> <c>AllowDrop</c> était posé, les gestionnaires
/// écrits et corrects, et pourtant rien ne tombait jamais dans la fenêtre de l'assistant — au point
/// que la panne a été signalée trois fois. La cause est ailleurs : SteamXBox.Desktop est déclaré
/// <c>requireAdministrator</c> dans son manifeste, l'Explorateur de Windows tourne sans élévation,
/// et l'isolation des privilèges d'interface (UIPI) interdit à un processus de niveau inférieur
/// d'envoyer des messages à une fenêtre de niveau supérieur. Le dépôt était bloqué par Windows
/// avant d'atteindre la moindre ligne de WPF, sans erreur, sans trace, sans curseur de refus.
///
/// <para>
/// Trois messages portent un glisser-déposer, et il faut les laisser passer tous les trois :
/// <c>WM_DROPFILES</c> annonce le dépôt, <c>WM_COPYDATA</c> et le <c>0x0049</c> non documenté
/// transportent les données elles-mêmes. En autoriser deux sur trois donne une fenêtre qui accepte
/// le survol puis ne reçoit rien — pire que le refus franc, car cela ressemble à un bogue du
/// produit.
/// </para>
///
/// <para>
/// <b>Ce que cela ouvre, et pourquoi c'est acceptable.</b> Le filtre est levé pour ces trois
/// messages seulement, et sur une seule fenêtre à la fois — pas sur le processus. C'est le
/// mécanisme prévu par Windows pour ce cas exact. L'alternative honnête serait de ne pas exiger
/// l'élévation, mais elle est nécessaire ailleurs (HidHide, ViGEmBus), et retirer le glisser-déposer
/// reviendrait à demander à l'utilisateur de recopier un chemin à la main — la seule valeur qu'un
/// assistant ne peut pas inventer.
/// </para>
/// </remarks>
public static class GlisserDepose
{
    private const uint WmDropFiles = 0x0233;
    private const uint WmCopyData = 0x004A;
    private const uint WmCopyGlobalData = 0x0049;

    /// <summary>Autoriser ce message à traverser l'isolation des privilèges.</summary>
    private const uint Autoriser_ = 1;

    /// <summary>
    /// Laisse les dépôts atteindre cette fenêtre, même si le produit tourne en administrateur.
    /// </summary>
    /// <remarks>
    /// À appeler quand la fenêtre a une poignée, donc à <c>SourceInitialized</c> et pas avant :
    /// <see cref="WindowInteropHelper"/> rend <see cref="IntPtr.Zero"/> tant que la fenêtre n'est
    /// pas créée, et l'appel est alors sans effet — silencieusement.
    /// </remarks>
    public static void Autoriser(Window fenetre, Action<string>? journal = null)
    {
        var poignee = new WindowInteropHelper(fenetre).Handle;

        if (poignee == IntPtr.Zero)
        {
            journal?.Invoke("glisser-déposer : la fenêtre n'a pas encore de poignée.");

            return;
        }

        // Le résultat de chaque appel est journalisé, et ce n'est pas du zèle.
        //
        // Le dépôt a été signalé cassé trois fois. Deux diagnostics successifs ont été posés sans
        // trace : le premier accusait le code du dépôt, qui était correct ; le second tenait la
        // levée UIPI pour acquise, sans avoir jamais vérifié qu'elle avait abouti. Un correctif
        // qu'on ne peut pas constater n'est pas un correctif, c'est une hypothèse déployée.
        foreach (var message in new[] { WmDropFiles, WmCopyData, WmCopyGlobalData })
        {
            var passe = ChangeWindowMessageFilterEx(poignee, message, Autoriser_, IntPtr.Zero);

            journal?.Invoke(
                $"glisser-déposer : message 0x{message:X4} "
                + (passe
                    ? "autorisé."
                    : $"REFUSÉ, erreur Windows {Marshal.GetLastWin32Error()}."));
        }

        journal?.Invoke(
            $"glisser-déposer : élévation = {Eleve()}, AllowDrop = {fenetre.AllowDrop}.");
    }

    /// <summary>Le produit tourne-t-il en administrateur ?</summary>
    /// <remarks>
    /// C'est toute la question : sans élévation, l'isolation des privilèges ne s'applique pas et le
    /// dépôt devrait fonctionner sans rien faire. Le savoir évite de chercher une heure du côté de
    /// WPF un blocage qui vient du manifeste.
    /// </remarks>
    private static bool Eleve()
    {
        try
        {
            using var moi = System.Security.Principal.WindowsIdentity.GetCurrent();

            return new System.Security.Principal.WindowsPrincipal(moi)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(
        IntPtr fenetre, uint message, uint action, IntPtr change);
}
