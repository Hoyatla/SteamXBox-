using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SenSÉ.EditeurTexte.UI;

public enum RulerOrientation { Horizontal, Vertical }

/// <summary>
/// Regle graphique (horizontale ou verticale) affichee au-dessus et a gauche
/// du RichTextBox. Pas d'ancres draggables en G.3 - juste l'affichage des
/// graduations. Phase G.4 prevoit des ancres (tab stops, marges editables).
/// </summary>
public class Ruler : FrameworkElement
{
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(RulerOrientation), typeof(Ruler),
        new FrameworkPropertyMetadata(RulerOrientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LengthProperty = DependencyProperty.Register(
        nameof(Length), typeof(double), typeof(Ruler),
        new FrameworkPropertyMetadata(2000.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public RulerOrientation Orientation
    {
        get => (RulerOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Longueur visuelle de la regle (en pixels). Defaut 2000.</summary>
    public double Length
    {
        get => (double)GetValue(LengthProperty);
        set => SetValue(LengthProperty, value);
    }

    // Constantes de rendu (en DIP = 1/96 de pouce).
    private const double MinorInterval = 10;     // petit tick
    private const double MajorInterval = 50;     // tick + label
    private const double MajorTickHeight = 6;
    private const double MinorTickHeight = 3;
    private const double LabelFontSize = 9;

    static Ruler()
    {
        // Le brush est en lecture seule apres Freeze(), performance OK.
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bg = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
        bg.Freeze();
        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var penMinor = new Pen(Brushes.Gray, 0.5);
        penMinor.Freeze();
        var penMajor = new Pen(Brushes.Black, 0.7);
        penMajor.Freeze();
        var labelBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));
        labelBrush.Freeze();
        var tf = new Typeface("Segoe UI");

        if (Orientation == RulerOrientation.Horizontal) RenderHorizontal(dc, penMinor, penMajor, labelBrush, tf);
        else RenderVertical(dc, penMinor, penMajor, labelBrush, tf);
    }

    private void RenderHorizontal(DrawingContext dc, Pen penMinor, Pen penMajor, Brush labelBrush, Typeface tf)
    {
        double w = Math.Min(ActualWidth, Length);
        double h = ActualHeight;
        // Ticks mineurs.
        for (double x = 0; x <= w; x += MinorInterval)
            dc.DrawLine(penMinor, new Point(x, h - MinorTickHeight), new Point(x, h));
        // Ticks majeurs + label tous les 50 px.
        for (double x = 0; x <= w; x += MajorInterval)
        {
            dc.DrawLine(penMajor, new Point(x, h - MajorTickHeight), new Point(x, h));
            if (x > 0)
            {
                var ft = new FormattedText(((int)x).ToString(),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    tf, LabelFontSize, labelBrush, 1.0);
                dc.DrawText(ft, new Point(x + 1, 0));
            }
        }
    }

    private void RenderVertical(DrawingContext dc, Pen penMinor, Pen penMajor, Brush labelBrush, Typeface tf)
    {
        double h = Math.Min(ActualHeight, Length);
        double w = ActualWidth;
        // Ticks mineurs (lignes horizontales).
        for (double y = 0; y <= h; y += MinorInterval)
            dc.DrawLine(penMinor, new Point(w - MinorTickHeight, y), new Point(w, y));
        // Ticks majeurs + label.
        for (double y = 0; y <= h; y += MajorInterval)
        {
            dc.DrawLine(penMajor, new Point(w - MajorTickHeight, y), new Point(w, y));
            if (y > 0)
            {
                // On dessine le label rotate de -90 degres (lecture verticale).
                var ft = new FormattedText(((int)y).ToString(),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    tf, LabelFontSize, labelBrush, 1.0);
                // Position de depart avec rotation autour du coin haut-gauche du texte.
                dc.PushTransform(new RotateTransform(-90, w - MajorTickHeight - 2, y - ft.Width / 2));
                dc.DrawText(ft, new Point(w - MajorTickHeight - 2, y - ft.Width / 2));
                dc.Pop();
            }
        }
    }
}
