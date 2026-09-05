using System.Windows;
using SenSÉ.Desktop.Assistant;
using SenSÉ.Mcp.Bus;

namespace SenSÉ.Desktop.Observateurs;

/// <summary>
/// Ecoute l'EventBus. Quand un evenement arrive et qu'une AssistantWindow
/// est ouverte, delegué a celle-ci pour qu'elle reagisse.
/// </summary>
/// <remarks>
/// <b>Pourquoi dans SenSÉ.Desktop et pas dans l'Assistant.</b> Le runner
/// depend de WPF (Application.Current.Windows), donc il vit dans le
/// projet qui dessine, pas dans la bibliothèque de logique.
///
/// <para><b>Pourquoi pas dans le constructeur d'AssistantWindow.</b>
/// Pour qu'on puisse demarrer un seul runner au demarrage de SenSÉ, et
/// qu'il s'applique a toute AssistantWindow qui s'ouvre. Si on le mettait
/// dans le constructeur, il faudrait le rebrancher a chaque ouverture.</para>
/// </remarks>
public sealed class ProactifRunner : IDisposable
{
    private readonly EventBus _bus;
    private readonly Action<string>? _journal;
    private readonly System.Windows.Threading.DispatcherTimer _pompe;
    private bool _dispose;

    public ProactifRunner(EventBus bus, Action<string>? journal = null)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _journal = journal;

        // Une pompe qui draine le bus toutes les 250ms. Pas de thread
        // dedie, pas de Channel : on est dans le fil WPF, on demande au
        // repartiteur de revisiter.
        _pompe = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _pompe.Tick += (_, _) => Drainer();
    }

    public void Demarrer()
    {
        _pompe.Start();
        _journal?.Invoke("ProactifRunner: en ecoute sur l'EventBus");
    }

    private void Drainer()
    {
        if (_dispose) return;

        var pris = _bus.Drainer();
        if (pris.Count == 0) return;

        var fenetre = Application.Current?.Windows.OfType<AssistantWindow>().FirstOrDefault();
        if (fenetre is null)
        {
            // Pas d'assistant ouvert, les evenements sont perdus pour cette fois.
            // C'est la regle : sans fenetre ouverte, pas de proactivite.
            _journal?.Invoke($"ProactifRunner: {pris.Count} evenement(s) ignore(s), Assistant non ouvert");
            return;
        }

        foreach (var evenement in pris)
        {
            fenetre.SurEvenementProactif(evenement, _journal);
        }
    }

    public void Dispose()
    {
        _dispose = true;
        _pompe.Stop();
    }
}