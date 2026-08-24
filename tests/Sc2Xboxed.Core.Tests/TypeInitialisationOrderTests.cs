using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Que les reglages de famille se construisent quel que soit le type touche en premier.
/// </summary>
/// <remarks>
/// <b>Le defaut.</b> <c>SteamControllerDefaults.Settings</c> etait un champ statique valant
/// <c>Sc2XboxedProfileSettings.Bare with { ... }</c>, et l'initialiseur d'instance de cet
/// enregistrement appelle <c>XboxButtonMap.Default</c>, qui lit <c>SteamControllerDefaults.LeftSide</c>.
/// Deux initialiseurs de type qui s'appellent l'un l'autre : entrer par <c>Settings</c> allait bien,
/// entrer par <c>Bare</c> lisait un <c>Bare</c> encore a null et levait une NullReferenceException.
/// L'initialiseur de type reste alors marque en echec pour toute la vie du processus, donc les
/// soixante-huit tests qui touchent la correspondance des boutons tombaient ensemble.
///
/// <para>
/// <b>Pourquoi un contexte de chargement separe.</b> L'ordre en cause est celui du processus entier,
/// et il est joue une seule fois : au moment ou un test ordinaire s'execute, ces types sont deja
/// initialises et n'importe quelle assertion passerait. Un <see cref="AssemblyLoadContext"/> neuf
/// charge une deuxieme copie de l'assemblage, avec ses propres statiques — ce qui rend l'ordre
/// d'entree choisissable, et donc le defaut reproductible a volonte au lieu d'une fois sur vingt.
/// </para>
///
/// <para>
/// Aucune horloge, aucune attente, aucun parallelisme a esperer : ces tests echouent a tous les
/// coups sur le code d'avant et passent a tous les coups sur celui d'apres.
/// </para>
/// </remarks>
public class TypeInitialisationOrderTests
{
    private const string SettingsTypeName = "Sc2Xboxed.Core.Mapping.Sc2XboxedProfileSettings";
    private const string SteamTypeName = "Sc2Xboxed.Core.Mapping.SteamControllerDefaults";
    private const string Ps5TypeName = "Sc2Xboxed.Core.Mapping.Ps5ControllerDefaults";
    private const string XboxTypeName = "Sc2Xboxed.Core.Mapping.XboxControllerDefaults";
    private const string MapTypeName = "Sc2Xboxed.Core.Mapping.XboxButtonMap";

    /// <summary>Une copie fraiche de Sc2Xboxed.Core, statiques comprises.</summary>
    private sealed class FreshCopy : AssemblyLoadContext, IDisposable
    {
        private readonly Assembly _core;

        public FreshCopy()
            : base(isCollectible: true)
            => _core = LoadFromAssemblyPath(Beside("Sc2Xboxed.Core.dll"));

        public Type Type(string fullName) => _core.GetType(fullName, throwOnError: true)!;

        /// <summary>
        /// Lit une propriete ou un champ statique, en laissant remonter ce que l'initialiseur de
        /// type a lance.
        /// </summary>
        /// <remarks>
        /// La reflexion emballe tout dans une <see cref="TargetInvocationException"/>, y compris
        /// l'echec qui nous interesse. Elle est deballee ici pour que l'assertion parle du defaut
        /// et non du moyen de l'atteindre.
        /// </remarks>
        public object? Read(string typeName, string memberName)
        {
            var type = Type(typeName);
            const BindingFlags Where = BindingFlags.Public | BindingFlags.Static;

            try
            {
                return type.GetProperty(memberName, Where)?.GetValue(null)
                       ?? type.GetField(memberName, Where)?.GetValue(null);
            }
            catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
            {
                throw wrapped.InnerException;
            }
        }

        public void Dispose() => Unload();

        /// <summary>
        /// Les dependances de l'assemblage viennent du dossier de sortie des tests, pas du contexte
        /// par defaut : les charger ailleurs rendrait la copie a moitie fraiche.
        /// </summary>
        protected override Assembly? Load(AssemblyName name)
        {
            var candidate = Beside(name.Name + ".dll");

            // Rendre null renvoie au contexte par defaut, ce qui est ce qu'il faut pour les
            // assemblages du framework : ils ne sont pas dans le dossier de sortie et n'ont aucun
            // etat statique en cause ici.
            return File.Exists(candidate) ? LoadFromAssemblyPath(candidate) : null;
        }

        private static string Beside(string fileName)
            => Path.Combine(AppContext.BaseDirectory, fileName);
    }

    /// <summary>
    /// L'ordre qui cassait. Entrer par l'enregistrement plutot que par une famille laissait son
    /// <c>Bare</c> a null au moment ou la famille le relisait.
    /// </summary>
    [Fact]
    public void TheSettingsRecordMayBeTouchedFirst()
    {
        using var fresh = new FreshCopy();

        Assert.NotNull(fresh.Read(SettingsTypeName, "Bare"));

        // Et les trois familles se construisent ensuite, ce qui est la moitie qui manquait : le
        // Bare lu par chacune doit etre celui qui vient d'etre construit, pas null.
        Assert.NotNull(fresh.Read(SteamTypeName, "Settings"));
        Assert.NotNull(fresh.Read(Ps5TypeName, "Settings"));
        Assert.NotNull(fresh.Read(XboxTypeName, "Settings"));
    }

    /// <summary>
    /// L'ordre qui passait deja. Conserve parce que c'est lui qui rendait le defaut intermittent :
    /// une correction qui casserait celui-ci echangerait simplement les deux moities du probleme.
    /// </summary>
    [Theory]
    [InlineData(SteamTypeName)]
    [InlineData(Ps5TypeName)]
    [InlineData(XboxTypeName)]
    public void AFamilyMayBeTouchedFirst(string family)
    {
        using var fresh = new FreshCopy();

        Assert.NotNull(fresh.Read(family, "Settings"));
        Assert.NotNull(fresh.Read(SettingsTypeName, "Bare"));
    }

    /// <summary>
    /// La correspondance des boutons aussi : c'est par elle que la plupart des soixante-huit tests
    /// entraient, et elle lit les tableaux d'une famille sans passer par ses reglages.
    /// </summary>
    [Fact]
    public void TheButtonMapMayBeTouchedFirst()
    {
        using var fresh = new FreshCopy();

        Assert.NotNull(fresh.Read(MapTypeName, "Default"));
        Assert.NotNull(fresh.Read(SettingsTypeName, "Bare"));
        Assert.NotNull(fresh.Read(SteamTypeName, "Settings"));
    }

    /// <summary>
    /// Les deux bouts a la fois, depuis plusieurs threads.
    /// </summary>
    /// <remarks>
    /// Un cycle rendu a la main dans un seul thread reste un cycle entre threads, et le CLR le
    /// rompt de la meme facon : il laisse un thread voir les statiques de l'autre non initialisees.
    /// Ce test ne prouve rien a lui seul — il ne peut qu'attraper un cycle reintroduit, pas garantir
    /// son absence — mais c'est la forme sous laquelle le defaut se presentait vraiment, une serie
    /// de tests sur un thread par coeur.
    /// </remarks>
    [Fact]
    public async Task BothEndsAtOnceAcrossThreads()
    {
        using var fresh = new FreshCopy();

        var entries = new (string Type, string Member)[]
        {
            (SettingsTypeName, "Bare"),
            (SteamTypeName, "Settings"),
            (Ps5TypeName, "Settings"),
            (XboxTypeName, "Settings"),
            (MapTypeName, "Default"),
        };

        // Tous relaches ensemble : l'interet est que plusieurs threads entrent dans les
        // initialiseurs de type en meme temps, pas qu'ils les traversent l'un apres l'autre.
        using var start = new ManualResetEventSlim(false);

        var readers = entries
            .Select(entry => Task.Run(() =>
            {
                start.Wait();
                return fresh.Read(entry.Type, entry.Member);
            }))
            .ToArray();

        start.Set();

        Assert.All(await Task.WhenAll(readers), Assert.NotNull);
    }
}
