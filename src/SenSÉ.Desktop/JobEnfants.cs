using System.Diagnostics;
using System.Runtime.InteropServices;
using SenSÉ.Core.Diagnostics;

namespace SenSÉ.Desktop;

/// <summary>
/// Le job Windows auquel sont inscrits les processus que SenSÉ démarre, pour qu'aucun ne lui
/// survive.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci corrige.</b> <see cref="App.OnExit"/> tue déjà ses enfants, et le fait
/// bien — mais il ne s'exécute que si l'environnement se ferme proprement. Tué depuis le
/// gestionnaire des tâches, planté, ou remplacé pendant qu'il tourne, il laisse tout derrière lui.
/// Mesuré le 6 septembre 2026, deux fois dans la même soirée : un <c>pc-agent</c> orphelin gardant
/// le port 8765 et empêchant le remplacement de son propre binaire, puis un <c>SenSÉ-Moniteur</c>
/// orphelin qui a rendu le bureau entier poussif. Ce dernier porte un crochet d'entrée bas niveau :
/// chaque clic et chaque frappe de la session passaient par un processus dont plus personne
/// n'attendait de réponse. La machine était libre — processeur au repos, dix-huit gigaoctets
/// disponibles, disque à deux pour cent — et réduire une fenêtre était impossible.
///
/// <para><b>Pourquoi un job et pas un <c>finally</c> de plus.</b> Un <c>finally</c> exprime une
/// intention, et une intention ne s'exécute pas quand le processus est tué. Le job est une
/// garantie du noyau : les enfants y sont inscrits à leur naissance, le handle vit aussi longtemps
/// que le processus, et <c>KILL_ON_JOB_CLOSE</c> fait tuer par Windows tout ce qu'il contient à la
/// fermeture du dernier handle — fermeture propre, plantage ou <c>taskkill /F</c> compris. Il n'y a
/// pas de chemin de sortie qui laisse un orphelin.</para>
///
/// <para><b>Ce qu'on n'y inscrit pas.</b> Le successeur lancé lors d'un changement de thème, qui
/// doit précisément survivre à celui qui le lance ; et les fichiers de l'utilisateur ouverts avec
/// leur application par défaut, qui ne nous appartiennent pas. Le job vaut pour l'infrastructure de
/// SenSÉ — le pont manette, les serveurs MCP, les outils du produit — pas pour ce que
/// l'utilisateur a ouvert.</para>
///
/// <para><b>Silencieux en cas d'échec.</b> Si la création du job échoue, l'inscription devient
/// sans effet et le démarrage continue : on retombe sur le comportement d'avant, qui marchait dans
/// le cas courant. Un environnement qui refuse de démarrer parce qu'il n'a pas pu se prémunir
/// contre les orphelins serait un remède pire que le mal.</para>
/// </remarks>
internal static class JobEnfants
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributs, string? nom);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job, uint classe, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int taille);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr processus);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Le handle du job, gardé pour toute la vie du processus.
    /// </summary>
    /// <remarks>
    /// Ne jamais le fermer : c'est sa fermeture qui déclenche la mise à mort, et on la veut au
    /// moment où ce processus disparaît, pas avant. Le handle n'est pas héritable — celui que
    /// rend <c>CreateJobObject</c> sans attributs de sécurité ne l'est pas — ce qui compte : un
    /// enfant qui en hériterait maintiendrait le job ouvert après notre mort, et le job ne
    /// tuerait plus personne.
    /// </remarks>
    private static readonly Lazy<IntPtr> Job = new(Creer, isThreadSafe: true);

    private static IntPtr Creer()
    {
        try
        {
            var job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                UiLog.Warn($"job des enfants : CreateJobObject a echoue ({Marshal.GetLastWin32Error()}). "
                    + "Les processus enfants pourront survivre a une fermeture brutale.");
                return IntPtr.Zero;
            }

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
                },
            };

            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(info)))
            {
                UiLog.Warn($"job des enfants : SetInformationJobObject a echoue ({Marshal.GetLastWin32Error()}).");
                CloseHandle(job);
                return IntPtr.Zero;
            }

            UiLog.Info("job des enfants cree : tout processus inscrit mourra avec l'environnement.");
            return job;
        }
        catch (Exception ex)
        {
            UiLog.Failure("creation du job des enfants", ex);
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Inscrit un processus au job. Sans effet si le job n'a pas pu être créé, ou si le processus
    /// est déjà terminé.
    /// </summary>
    /// <remarks>
    /// À appeler juste après <c>Process.Start</c>. Il reste un intervalle — entre le démarrage et
    /// l'inscription — pendant lequel un enfant qui se lancerait lui-même des petits-enfants les
    /// laisserait hors du job. Le fermer demanderait de créer le processus suspendu, donc de
    /// remplacer <c>Process.Start</c> par un <c>CreateProcess</c> natif. Aucun des serveurs de
    /// SenSÉ n'ouvre quoi que ce soit dans ses premières millisecondes ; le jour où l'un le fera,
    /// c'est ici qu'il faudra revenir.
    /// </remarks>
    public static void Inscrire(Process? processus)
    {
        if (processus is null) return;

        var job = Job.Value;
        if (job == IntPtr.Zero) return;

        try
        {
            if (processus.HasExited) return;
            if (!AssignProcessToJobObject(job, processus.Handle))
            {
                UiLog.Warn($"job des enfants : PID {processus.Id} non inscrit "
                    + $"({Marshal.GetLastWin32Error()}). Il pourra survivre a une fermeture brutale.");
            }
        }
        catch (Exception ex)
        {
            UiLog.Failure($"inscription au job des enfants", ex);
        }
    }
}
