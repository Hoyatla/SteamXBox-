using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class VuePort : UserControl
{
    public Port? Port { get; private set; }
    public bool EstEntree { get; }

    public VuePort(Port port)
    {
        EstEntree = port.EstEntree;
        Port = port;
        InitializeComponent();
        var color = (Color)ColorConverter.ConvertFromString(port.Type.Couleur());
        Cercle.Fill = new SolidColorBrush(color);
        Cercle.Stroke = new SolidColorBrush(color);
        ToolTip = $"{port.Nom} ({port.Type})";
    }
}