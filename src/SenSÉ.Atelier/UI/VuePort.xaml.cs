using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class VuePort : UserControl
{
    public Port? Port { get; private set; }
    public Noeud? ParentNoeud { get; private set; }
    public bool EstEntree { get; private set; }

    public VuePort() { InitializeComponent(); }

    public VuePort(Port port) : this()
    {
        Port = port;
        EstEntree = port.EstEntree;
        var color = (Color)ColorConverter.ConvertFromString(port.Type.Couleur());
        Cercle.Fill = new SolidColorBrush(color);
        Cercle.Stroke = new SolidColorBrush(color);
        ToolTip = port.Nom + " (" + port.Type + ")";
    }

    public void AttacherParent(Noeud n, Port p)
    {
        ParentNoeud = n;
        if (Port is null)
        {
            Port = p;
            EstEntree = p.EstEntree;
            var color = (Color)ColorConverter.ConvertFromString(p.Type.Couleur());
            Cercle.Fill = new SolidColorBrush(color);
            Cercle.Stroke = new SolidColorBrush(color);
            ToolTip = p.Nom + " (" + p.Type + ")";
        }
    }

    private void Cercle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ParentNoeud is null || Port is null) return;
        // Seuls les ports de sortie peuvent initier un drag
        if (Port.EstEntree) return;
        var data = new DataObject();
        data.SetData(CanvasAtelier.DragFormatPort, new PortRef(ParentNoeud.Id, Port.Nom, false));
        try { DragDrop.DoDragDrop(Cercle, data, DragDropEffects.Link); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("drag port echoue: " + ex.Message); }
    }

    private void Cercle_DragEnter(object sender, DragEventArgs e)
    {
        if (ParentNoeud is null || Port is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (!e.Data.GetDataPresent(CanvasAtelier.DragFormatPort)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var src = e.Data.GetData(CanvasAtelier.DragFormatPort) as PortRef;
        if (src is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        // Cible doit etre entree, source sortie, types compatibles, noeuds differents
        if (!Port.EstEntree) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        if (src.NoeudId == ParentNoeud.Id) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var srcPort = TrouverPortSortie(src.NoeudId, src.PortNom);
        if (srcPort is null || !srcPort.Type.Compatible(Port.Type)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Link;
        e.Handled = true;
    }

    private void Cercle_Drop(object sender, DragEventArgs e)
    {
        if (ParentNoeud is null || Port is null) { e.Handled = true; return; }
        if (!e.Data.GetDataPresent(CanvasAtelier.DragFormatPort)) { e.Handled = true; return; }
        var src = e.Data.GetData(CanvasAtelier.DragFormatPort) as PortRef;
        if (src is null) { e.Handled = true; return; }
        if (!Port.EstEntree) { e.Handled = true; return; }
        if (src.NoeudId == ParentNoeud.Id) { e.Handled = true; return; }
        // Remonter jusqu'au CanvasAtelier via VisualParent
        var cnv = RemonterCanvas();
        if (cnv is null) { e.Handled = true; return; }
        var srcNoeud = cnv.Graphe?.TrouverNoeud(src.NoeudId);
        var cblNoeud = cnv.Graphe?.TrouverNoeud(ParentNoeud.Id);
        if (srcNoeud is null || cblNoeud is null) { e.Handled = true; return; }
        try { cnv.CreerLien(srcNoeud, src.PortNom, cblNoeud, Port.Nom); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("creer lien echoue: " + ex.Message); }
        e.Handled = true;
    }

    private Port? TrouverPortSortie(string noeudId, string portNom)
    {
        var cnv = RemonterCanvas();
        var n = cnv?.Graphe?.TrouverNoeud(noeudId);
        return n?.PortsSortie.FirstOrDefault(p => p.Nom == portNom);
    }

    private CanvasAtelier? RemonterCanvas()
    {
        DependencyObject? d = this;
        while (d is not null)
        {
            if (d is CanvasAtelier c) return c;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}

[Serializable]
public sealed class PortRef
{
    public string NoeudId { get; set; } = "";
    public string PortNom { get; set; } = "";
    public bool EstSortie { get; set; }
    public PortRef() { }
    public PortRef(string noeudId, string portNom, bool estSortie)
    {
        NoeudId = noeudId;
        PortNom = portNom;
        EstSortie = estSortie;
    }
}