using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

/// <summary>
/// Mini-carte du graphe. Affiche les noeuds en mini (scale 0.1) + un
/// rectangle representant la viewport. 200x150px, placee en bas a droite
/// du canvas.
/// </summary>
public partial class MiniMap : UserControl
{
    private double _scale = 0.1;
    private double _contentWidth = 1000;
    private double _contentHeight = 600;
    private double _viewportX, _viewportY, _viewportW, _viewportH;

    public MiniMap()
    {
        InitializeComponent();
    }

    /// <summary>Met a jour la mini-map avec le graphe et la viewport courante.</summary>
    public void MettreAJour(Graphe? g, Rect viewport)
    {
        MapSurface.Children.Clear();
        if (g is null || g.Noeuds.Count == 0)
        {
            _contentWidth = ActualWidth > 0 ? ActualWidth : 200;
            _contentHeight = ActualHeight > 0 ? ActualHeight : 150;
            _scale = 1.0;
            return;
        }

        // Calculer la bbox
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in g.Noeuds)
        {
            if (n.X < minX) minX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.X + 140 > maxX) maxX = n.X + 140;
            if (n.Y + 90 > maxY) maxY = n.Y + 90;
        }
        // Pour la viewport (si on zoom out, on peut avoir des coords negatives)
        if (viewport.X < minX) minX = viewport.X;
        if (viewport.Y < minY) minY = viewport.Y;
        if (viewport.Right > maxX) maxX = viewport.Right;
        if (viewport.Bottom > maxY) maxY = viewport.Bottom;
        // Marge
        minX -= 100; minY -= 100; maxX += 100; maxY += 100;
        _contentWidth = Math.Max(1, maxX - minX);
        _contentHeight = Math.Max(1, maxY - minY);

        // Scale pour fitter dans 200x150 (avec une petite marge)
        var sx = (ActualWidth - 8) / _contentWidth;
        var sy = (ActualHeight - 8) / _contentHeight;
        _scale = Math.Min(sx, sy);
        if (_scale <= 0) _scale = 0.01;

        // Dessiner les noeuds
        foreach (var n in g.Noeuds)
        {
            var r = new Rectangle
            {
                Width = 140 * _scale,
                Height = 90 * _scale,
                Fill = new SolidColorBrush(Color.FromRgb(0x8F, 0xB4, 0xFF)),
                Stroke = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xF4)),
                StrokeThickness = 0.5,
            };
            Canvas.SetLeft(r, (n.X - minX) * _scale);
            Canvas.SetTop(r, (n.Y - minY) * _scale);
            MapSurface.Children.Add(r);
        }

        // Dessiner les liens
        foreach (var l in g.Liens)
        {
            var src = g.Noeuds.FirstOrDefault(n => n.Id == l.NoeudSourceId);
            var dst = g.Noeuds.FirstOrDefault(n => n.Id == l.NoeudCibleId);
            if (src is null || dst is null) continue;
            var x1 = (src.X - minX + 70) * _scale;
            var y1 = (src.Y - minY + 45) * _scale;
            var x2 = (dst.X - minX + 70) * _scale;
            var y2 = (dst.Y - minY + 45) * _scale;
            var line = new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = new SolidColorBrush(Color.FromRgb(0x93, 0x93, 0xB8)), StrokeThickness = 0.5 };
            MapSurface.Children.Add(line);
        }

        // Viewport rect
        _viewportX = (viewport.X - minX) * _scale;
        _viewportY = (viewport.Y - minY) * _scale;
        _viewportW = viewport.Width * _scale;
        _viewportH = viewport.Height * _scale;
        if (_viewportW > ActualWidth - 4) _viewportW = ActualWidth - 4;
        if (_viewportH > ActualHeight - 4) _viewportH = ActualHeight - 4;
        if (_viewportX < 0) _viewportX = 0;
        if (_viewportY < 0) _viewportY = 0;
        Canvas.SetLeft(ViewportRect, _viewportX);
        Canvas.SetTop(ViewportRect, _viewportY);
        ViewportRect.Width = _viewportW;
        ViewportRect.Height = _viewportH;
    }
}
