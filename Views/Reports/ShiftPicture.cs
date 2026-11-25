using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using static Raphael.Desktop.Views.Reports.CalcUi;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>
    /// RE-092: the paid shift, drawn: the real shift, what is paid and what is not, each with its total,
    /// and under them how the start and the end came out. The legend shows an example; a day, its own.
    /// </summary>
    public sealed class ShiftPicture : StackPanel
    {
        public static readonly DependencyProperty ShiftProperty = DependencyProperty.Register(nameof(Shift), typeof(ShiftCalculationDto), typeof(ShiftPicture),
            new PropertyMetadata(null, (d, _) => ((ShiftPicture)d).Draw()));

        public ShiftCalculationDto? Shift { get => (ShiftCalculationDto?)GetValue(ShiftProperty); set => SetValue(ShiftProperty, value); }

        /// <summary>The legend's: one morning start too early and one late Pull-in.</summary>
        public static ShiftCalculationDto Example()
        {
            static DateTime At(int h, int m) => DateTime.Today.AddHours(h).AddMinutes(m).ToUniversalTime();
            return new ShiftCalculationDto
            {
                FirstCommitmentUtc = At(8, 0), DriveOutMinutes = 34, PreparationMinutes = 20, InspectionMinutes = 10,
                AllowedStartUtc = At(6, 56), RealStartUtc = At(6, 8), PaidStartUtc = At(6, 56),
                LastPerformUtc = At(16, 41), DriveBackMinutes = 31, AllowedEndUtc = At(17, 22), RealEndUtc = At(18, 10), PaidEndUtc = At(17, 22),
                RealMinutes = 722, PaidMinutes = 626, StartUnpaidMinutes = 48, EndUnpaidMinutes = 48
            };
        }

        private static string H(DateTime? d) => WarningLook.Hour(d);
        private static string Dur(int minutes) => ProductivityFormat.Hours(minutes);

        private void Draw()
        {
            Children.Clear();
            if (Shift is not { RealStartUtc: DateTime start, RealEndUtc: DateTime end } s || end <= start) return;
            if (s.PaidStartUtc is not DateTime paidStart || s.PaidEndUtc is not DateTime paidEnd)
            {
                Children.Add(Text(T("calc.Old"), 13, Mute, false, TextAlignment.Center));
                return;
            }

            var (a, b, c) = ((double)s.StartUnpaidMinutes, (double)Math.Max(1, s.PaidMinutes), (double)s.EndUnpaidMinutes);
            var unpaid = s.StartUnpaidMinutes + s.EndUnpaidMinutes;

            Children.Add(Row(T("calc.Real"), new[] { new Part(a + b + c, Grey, $"{H(start)} – {H(end)}") }, Dur(s.RealMinutes)));
            Children.Add(Row(T("calc.Unpaid"), new[]
            {
                new Part(a, Red, Dur(s.StartUnpaidMinutes), $"{H(start)} – {H(paidStart)}"), new Part(b, null),
                new Part(c, Red, Dur(s.EndUnpaidMinutes), $"{H(paidEnd)} – {H(end)}")
            }, unpaid == 0 ? "0:00" : "−" + Dur(unpaid), Red));
            Children.Add(Row(T("calc.Paid"), new[] { new Part(a, null), new Part(b, Violet, $"{H(paidStart)} – {H(paidEnd)}"), new Part(c, null) }, Dur(s.PaidMinutes), Violet));
            Children.Add(Equation(("", Dur(s.RealMinutes), T("calc.Real"), Ink), ("−", Dur(unpaid), T("calc.Unpaid"), Red), ("=", Dur(s.PaidMinutes), T("calc.Paid"), Violet)));

            Children.Add(new Border { Margin = new Thickness(0, 10, 0, 0), Child = Columns(StartReceipt(s), EndReceipt(s)) });
        }

        private static UIElement StartReceipt(ShiftCalculationDto s)
        {
            var lines = new List<(string, string, bool)>();
            if (s.FirstCommitmentUtc is not null && s.DriveOutMinutes is int drive && s.AllowedStartUtc is not null)
            {
                lines.Add((T("calc.Commitment"), H(s.FirstCommitmentUtc), false));
                lines.Add((T("calc.DriveOut"), $"− {drive} min", false));
                lines.Add((T("calc.Prep"), $"− {s.PreparationMinutes} min", false));
                if (s.InspectionMinutes > 0) lines.Add((T("calc.Insp"), $"− {s.InspectionMinutes} min", false));
            }
            if (s.AllowedStartUtc is not null) lines.Add((T("calc.AllowedStart"), H(s.AllowedStartUtc), lines.Count > 0));
            lines.Add((T("calc.RealStart"), H(s.RealStartUtc), false));
            lines.Add((T("calc.PaidStart"), H(s.PaidStartUtc), true));
            return Receipt(T("calc.Start"), PackIconKind.ClockStart, lines, T(s.AllowedStartUtc is null ? "calc.NoLimitStart" : "calc.Later"));
        }

        private static UIElement EndReceipt(ShiftCalculationDto s)
        {
            var lines = new List<(string, string, bool)>();
            if (s.LastPerformUtc is not null && s.DriveBackMinutes is int back && s.AllowedEndUtc is not null)
            {
                lines.Add((T("calc.LastPerform"), H(s.LastPerformUtc), false));
                lines.Add((T("calc.DriveBack"), $"+ {back} min", false));
                if (s.InspectionMinutes > 0) lines.Add((T("calc.Insp"), $"+ {s.InspectionMinutes} min", false));
            }
            if (s.AllowedEndUtc is not null) lines.Add((T("calc.AllowedEnd"), H(s.AllowedEndUtc), lines.Count > 0));
            lines.Add((T("calc.RealEnd"), H(s.RealEndUtc), false));
            lines.Add((T("calc.PaidEnd"), H(s.PaidEndUtc), true));
            return Receipt(T("calc.End"), PackIconKind.ClockEnd, lines, T(s.AllowedEndUtc is null ? "calc.NoLimitEnd" : "calc.Earlier"));
        }
    }
}
