using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>The drawing that explains each warning in the legend (RE-092). Example figures, no data.</summary>
    public sealed class WarningPicture : Viewbox
    {
        public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(WarningPicture),
            new PropertyMetadata(null, (d, e) => ((WarningPicture)d).Draw()));

        public string? Code { get => (string?)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

        private const string Violet = "#673AB7", Red = "#B3261E", Amber = "#C2410C", Blue = "#0EA5E9", Green = "#1B7F3B", Grey = "#5B6B7C", Soft = "#DADDE8";
        private Canvas _c = new();

        public WarningPicture() { Stretch = Stretch.Uniform; StretchDirection = StretchDirection.DownOnly; HorizontalAlignment = HorizontalAlignment.Left; }

        private void Draw()
        {
            _c = new Canvas { Width = 560, Height = 150 };
            switch (Code)
            {
                case "far_from_stop":
                    Circle(130, 72, 52, "#DCF2E3", Green, true); Dot(130, 72, Violet); Line(140, 418, 72, Amber, 2, true); Dot(428, 72, Amber);
                    Text(130, 84, "Pickup"); Text(130, 128, "0.2 mi", Green); Text(428, 84, "Perform", Amber); Text(285, 46, "0.9 mi", Amber, true); break;
                case "outside_allowed_hours":
                    Line(60, 500, 70, "#E8B27A", 26); Line(140, 430, 70, Violet, 26);
                    Text(60, 30, "06:10", Amber); Text(140, 30, "07:25"); Text(430, 30, "16:40"); Text(500, 30, "17:30", Amber); Text(100, 92, "$ 0", Amber, true); Text(465, 92, "$ 0", Amber, true); break;
                case "off_route_miles":
                    Line(40, 170, 75, Violet, 6); Line(170, 390, 75, Amber, 6, true); Line(390, 520, 75, Violet, 6); Dot(170, 75, Violet); Dot(390, 75, Violet);
                    Text(170, 38, "Pull-in 120,400"); Text(390, 38, "Pull-out 120,560"); Text(280, 92, "160 mi", Amber, true); break;
                case "odometer_vs_gps": Bar(28, 330, Violet, WarningLook.T("productivity.ColMiles"), "300 mi"); Bar(72, 225, Blue, "GPS", "205 mi"); Text(285, 112, "46 %", Amber, true); break;
                case "pull_in_impossible": Bar(28, 28, Blue, "GPS", "205 mi"); Bar(72, 350, Red, WarningLook.T("productivity.ColMiles"), "2,900 mi"); Text(285, 112, "× 14", Red, true); break;
                case "pull_out_impossible":
                    Line(40, 520, 80, Soft, 8); Line(40, 200, 80, Green, 8); Line(200, 300, 80, Amber, 8); Line(300, 520, 80, Red, 8);
                    Text(40, 94, "0"); Text(200, 94, "100 mi"); Text(300, 94, "500 mi"); Text(410, 40, "3,149,661", Red, true); Dot(490, 80, Red); break;
                case "odometer_backwards":
                    Dot(150, 50, Violet); Dot(410, 95, Red); Slope(158, 52, 402, 93, Red); Text(150, 14, "Pull-out 287,800"); Text(410, 108, "Pull-in 287,040", Red); break;
                case "zero_miles_with_trips":
                    Dot(90, 75, Violet); Dot(470, 75, Violet); Line(100, 460, 75, Amber, 3, true); Text(90, 38, "888"); Text(470, 38, "888"); Text(280, 42, "GPS 223 mi", Amber, true); Text(280, 88, "0 mi"); break;
                case "wrong_vehicle":
                    Line(90, 520, 46, Violet, 4); Line(90, 520, 108, Amber, 4); Text(48, 36, "A", Grey, true); Text(48, 98, "B", Grey, true); Text(300, 18, "128,337 … 130,502"); Text(300, 80, "332,099 … 333,988", Amber); break;
                case "open_shift":
                    Line(60, 400, 75, Violet, 10); Line(400, 500, 75, Red, 4, true); Dot(60, 75, Violet); Text(60, 38, "Pull-out"); Text(500, 38, "Pull-in ?", Red, true); break;
                case "long_shift":
                    Bar(55, 380, "#E8B27A", "", "16 h 46"); Line(428, 430, 66, Amber, 56); Text(428, 98, "14 h", Amber, true); break;
                case "incomplete_gps": Line(50, 200, 75, Blue, 6); Line(200, 360, 75, "#0B67A3", 3, true); Line(360, 510, 75, Blue, 6); Text(125, 42, "GPS"); Text(435, 42, "GPS"); Text(280, 42, "?", "#0B67A3", true); break;
                case "no_return_leg": Dot(120, 75, Violet); Line(130, 420, 75, "#0B67A3", 3, true); Dot(430, 75, Violet); Text(120, 38, "Dropoff"); Text(430, 38, "Pull-in"); Text(280, 42, "?", "#0B67A3", true); break;
            }
            Child = _c;
        }

        private void Put(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); _c.Children.Add(e); }
        private void Dot(double x, double y, string color) => Put(new Ellipse { Width = 16, Height = 16, Fill = WarningLook.Of(color) }, x - 8, y - 8);

        private void Circle(double x, double y, double r, string fill, string line, bool dashed) => Put(new Ellipse
        {
            Width = 2 * r, Height = 2 * r, Fill = WarningLook.Of(fill), Stroke = WarningLook.Of(line), StrokeThickness = 1.5,
            StrokeDashArray = dashed ? new DoubleCollection { 4, 3 } : null
        }, x - r, y - r);

        private void Line(double x1, double x2, double y, string color, double width, bool dashed = false) => _c.Children.Add(new Line
        {
            X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = WarningLook.Of(color), StrokeThickness = width,
            StrokeDashArray = dashed ? new DoubleCollection { 3, 2 } : null
        });

        private void Slope(double x1, double y1, double x2, double y2, string color) =>
            _c.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = WarningLook.Of(color), StrokeThickness = 3 });

        private void Bar(double y, double width, string color, string label, string value)
        {
            var l = new TextBlock { Text = label, Foreground = WarningLook.Of(Grey), FontSize = 14 };
            Put(l, 8, y + 2);
            Put(new Border { Width = width, Height = 24, CornerRadius = new CornerRadius(3), Background = WarningLook.Of(color) }, 100, y);
            Put(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold }, 108 + width, y + 2);
        }

        private void Text(double x, double y, string text, string color = "#1E2A36", bool bold = false)
        {
            var t = new TextBlock { Text = text, FontSize = 14, Foreground = WarningLook.Of(color), FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Put(t, x - t.DesiredSize.Width / 2, y);
        }
    }
}
