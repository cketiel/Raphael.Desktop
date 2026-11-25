using MaterialDesignThemes.Wpf;
using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>The pieces the calculations are drawn with (RE-092), so hours and miles look the same.</summary>
    internal static class CalcUi
    {
        public const string Violet = "#673AB7", Deep = "#4527A0", Grey = "#B8C2CE", Red = "#DC5A50", Green = "#2E7D32", Ink = "#1E2A36", Mute = "#5B6B7C", Pale = "#D9CCF0";

        public static string T(string key) => WarningLook.T(key);
        public static string F(string key, params object[] args) => string.Format(T(key), args);

        public static TextBlock Text(string text, double size = 13, string color = Ink, bool bold = false, TextAlignment align = TextAlignment.Left, Thickness? margin = null) => new()
        {
            Text = text, FontSize = size, Foreground = WarningLook.Of(color), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = align, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0), VerticalAlignment = VerticalAlignment.Center
        };

        /// <param name="Weight">What it takes of the bar.</param>
        public sealed record Part(double Weight, string? Color, string Label = "", string Tip = "");

        /// <summary>A bar of parts, in proportion; a part with no colour is a gap. Its total goes at the right, in bold.</summary>
        public static Grid Row(string label, IEnumerable<Part> parts, string total, string totalColor = Ink, double labelWidth = 118, double scale = 1)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });

            row.Children.Add(Text(label, 12.5, Mute, true));
            var bar = new Grid { Height = 26 };
            var list = parts.Where(p => p.Weight > 0).ToList();
            foreach (var p in list) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p.Weight, GridUnitType.Star) });
            var used = list.Sum(p => p.Weight);
            if (scale < 1 && used > 0) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(used * (1 - scale) / Math.Max(scale, 0.001), GridUnitType.Star) });
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Color is not { } color) continue;
                var piece = new Border
                {
                    Background = WarningLook.Of(color), CornerRadius = new CornerRadius(4), Margin = new Thickness(i == 0 ? 0 : 1, 0, 0, 0), MinWidth = 2, ClipToBounds = true,
                    ToolTip = list[i].Tip.Length > 0 ? list[i].Tip : null,
                    Child = new TextBlock
                    {
                        Text = list[i].Label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = color is Grey or Pale ? WarningLook.Of(Ink) : Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.None
                    }
                };
                // A label that does not fit says nothing: the piece keeps it in its tooltip.
                piece.SizeChanged += (s, _) =>
                {
                    var b = (Border)s; var t = (TextBlock)b.Child;
                    t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    t.Visibility = t.DesiredSize.Width + 6 <= b.ActualWidth ? Visibility.Visible : Visibility.Hidden;
                };
                Grid.SetColumn(piece, i);
                bar.Children.Add(piece);
            }
            Grid.SetColumn(bar, 1);
            row.Children.Add(bar);
            var sum = Text(total, 16, totalColor, true, TextAlignment.Right);
            Grid.SetColumn(sum, 2);
            row.Children.Add(sum);
            return row;
        }

        /// <summary>"8:13 real − 0:18 not paid = 7:55 paid", as three boxes in the middle.</summary>
        public static UIElement Equation(params (string Sign, string Value, string Label, string Color)[] terms)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 4) };
            foreach (var t in terms)
            {
                if (t.Sign.Length > 0) panel.Children.Add(Text(t.Sign, 20, Mute, true, TextAlignment.Center, new Thickness(12, 0, 12, 0)));
                var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                box.Children.Add(Text(t.Value, 19, t.Color, true, TextAlignment.Center));
                box.Children.Add(Text(t.Label, 11.5, Mute, false, TextAlignment.Center));
                panel.Children.Add(new Border { Background = WarningLook.Of("#F6F3FC"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 7, 14, 7), MinWidth = 104, Child = box });
            }
            return new Viewbox { Child = panel, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Center };
        }

        /// <summary>A sum written as a receipt: one line per figure, the result under a rule.</summary>
        public static Border Receipt(string title, PackIconKind icon, IEnumerable<(string Label, string Value, bool Result)> lines, string? note)
        {
            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
            head.Children.Add(new PackIcon { Kind = icon, Width = 18, Height = 18, Foreground = WarningLook.Of(Violet), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Text(title, 14, Deep, true, TextAlignment.Center, new Thickness(7, 0, 0, 0)));
            body.Children.Add(head);
            foreach (var l in lines)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.Children.Add(Text(l.Label, 12.5, l.Result ? Deep : Ink, l.Result));
                var v = Text(l.Value, 13, l.Result ? Deep : Ink, true, TextAlignment.Right, new Thickness(10, 0, 0, 0));
                Grid.SetColumn(v, 1);
                g.Children.Add(v);
                body.Children.Add(l.Result ? new Border { BorderBrush = WarningLook.Of("#D1C4E9"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = g } : g);
            }
            if (note != null) body.Children.Add(Text(note, 11.5, Mute, false, TextAlignment.Center, new Thickness(0, 6, 0, 0)));
            return Card(body);
        }

        public static Border Card(UIElement child, Thickness? margin = null) => new()
        {
            Background = Brushes.White, BorderBrush = WarningLook.Of("#E3E8EF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12), Margin = margin ?? new Thickness(0), Child = child
        };

        public static TextBlock Heading(string text) => Text(text, 14.5, Deep, true, TextAlignment.Center, new Thickness(0, 0, 0, 8));

        public static UIElement Legend(params (string Color, string Label)[] items)
        {
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var i in items)
            {
                var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(9, 2, 9, 2) };
                p.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(3), Background = WarningLook.Of(i.Color), VerticalAlignment = VerticalAlignment.Center });
                p.Children.Add(Text(i.Label, 12, Mute, false, TextAlignment.Left, new Thickness(6, 0, 0, 0)));
                wrap.Children.Add(p);
            }
            return wrap;
        }

        public static UIElement Columns(params UIElement[] items)
        {
            var g = new Grid();
            for (var i = 0; i < items.Length; i++)
            {
                if (i > 0) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(items[i], i * 2);
                g.Children.Add(items[i]);
            }
            return g;
        }
    }
}
