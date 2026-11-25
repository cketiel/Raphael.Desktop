using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services.Help;
using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static Raphael.Desktop.Views.Reports.CalcUi;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>
    /// RE-092: how a shift's hours, or its miles, came out, with the day's own figures. The hours end in
    /// the paid shift, the miles in the miles with a patient on board: the two figures pay is made of.
    /// </summary>
    public sealed class DayCalculationWindow : Window
    {
        private readonly StackPanel _body = new() { Margin = new Thickness(22, 18, 22, 10) };
        private readonly string _topic;

        private DayCalculationWindow(ProductivityRow row, bool miles)
        {
            _topic = miles ? "desktop/reports/productivity-miles" : "desktop/reports/productivity-hours";
            HelpAssist.SetTopicId(this, _topic);
            Title = T(miles ? "calc.MilesTitle" : "calc.ShiftTitle");
            Owner = Application.Current?.MainWindow;
            Width = 820;
            MaxHeight = SystemParameters.WorkArea.Height - 40;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Background = WarningLook.Of("#F4F5FA");
            FontFamily = Application.Current?.MainWindow?.FontFamily ?? FontFamily;
            UseLayoutRounding = true;

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            head.Children.Add(Text(Title, 20, Deep, true, TextAlignment.Center));
            head.Children.Add(Text($"{row.Driver} · {row.Date:MM/dd/yyyy} · {row.Vehicle}", 13, Mute, false, TextAlignment.Center, new Thickness(0, 2, 0, 0)));
            _body.Children.Add(head);
            _body.Children.Add(Text(T("shift.Loading"), 13, Mute, false, TextAlignment.Center));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 14) };
            buttons.Children.Add(Action(PackIconKind.BookOpenVariant, T("info.MoreHelp"), () => HelpService.Instance.Open(_topic)));
            buttons.Children.Add(Action(PackIconKind.ShieldAlertOutline, T("info.Warnings"), () => WarningLegendWindow.ShowLegend(miles ? "odometer_vs_gps" : "outside_allowed_hours")));
            buttons.Children.Add(Action(PackIconKind.Close, T("warn.Close"), Close));
            DockPanel.SetDock(buttons, Dock.Bottom);

            var dock = new DockPanel();
            dock.Children.Add(buttons);
            dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body });
            Content = dock;
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            // It grows once the figures arrive: it stays in the middle of the screen.
            SizeChanged += (_, _) => { var a = SystemParameters.WorkArea; Left = a.Left + (a.Width - ActualWidth) / 2; Top = a.Top + (a.Height - ActualHeight) / 2; };
            Loaded += async (_, _) => await FillAsync(row, miles);
        }

        public static void ShowHours(ProductivityRow row) => new DayCalculationWindow(row, false).Show();
        public static void ShowMiles(ProductivityRow row) => new DayCalculationWindow(row, true).Show();

        private static Button Action(PackIconKind icon, string text, Action act)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new PackIcon { Kind = icon, Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = p, Width = 150, Height = 34, Margin = new Thickness(6, 0, 6, 0), Foreground = WarningLook.Of(Violet), BorderBrush = WarningLook.Of("#B39DDB") };
            if (Application.Current?.TryFindResource("MaterialDesignOutlinedButton") is Style style) b.Style = style;
            b.Click += (_, _) => act();
            return b;
        }

        private async Task FillAsync(ProductivityRow row, bool miles)
        {
            ProductivityDayWarningsDto? day = null;
            try { day = await row.DetailAsync(); }
            catch (Exception ex) { FileLogger.Log($"Productivity calculation: {ex.Message}"); }

            _body.Children.RemoveAt(1);
            if (miles ? day?.Miles is null : day?.Shift is null)
            {
                _body.Children.Add(Text(T("shift.Error"), 13, "#B3261E", true, TextAlignment.Center));
                return;
            }
            if (miles) Miles(row, day!.Miles!); else Hours(row, day!.Shift!);
        }

        private void Hours(ProductivityRow row, ShiftCalculationDto s)
        {
            _body.Children.Add(new ShiftPicture { Shift = s });
            if (s.PaidStartUtc is null) return;

            var groups = new StackPanel();
            groups.Children.Add(Heading(T("calc.Where")));
            groups.Children.Add(Row(T("calc.Paid"), row.HourSegments.Select(h => new Part(h.Width, ((SolidColorBrush)h.Color).Color.ToString(), h.Value, $"{h.Label} {h.Value}")), row.PaidText, Violet));
            groups.Children.Add(Legend(row.HourSegments.Select(h => (((SolidColorBrush)h.Color).Color.ToString(), $"{h.Label} {h.Value}")).ToArray()));
            groups.Children.Add(Text(T("calc.WhereHint"), 12, Mute, false, TextAlignment.Center, new Thickness(0, 6, 0, 0)));
            _body.Children.Add(Card(groups, new Thickness(0, 12, 0, 0)));

            Pay(row, DriverPayRateType.Hourly, row.PaidText + " h", T("calc.Paid"), "info.Ex.NotHour");
        }

        private void Miles(ProductivityRow row, MileCalculationDto m)
        {
            string N(double? v) => v is double x ? x.ToString("N1") : "—";
            var occupied = m.OccupiedMiles ?? 0;
            var empty = m.EmptyMiles ?? 0;
            var byRiders = m.Legs.Where(l => l.Patients > 0).GroupBy(l => l.Patients).OrderBy(g => g.Key)
                .Select(g => (Riders: g.Key, Value: g.Sum(l => l.PatientMiles))).Where(g => g.Value > 0).ToList();
            var max = new[] { occupied + empty, m.PatientMiles ?? 0, m.GpsMiles ?? 0, m.PlannedMiles ?? 0, 1 }.Max();
            double Scale(double v) => Math.Clamp(v / max, 0.001, 1);

            var bars = new StackPanel();
            bars.Children.Add(Row(T("calc.Odometer"), new[]
                {
                    new Part(occupied, Pale, $"{N(occupied)} {T("calc.Occupied")}", $"{N(occupied)} {T("calc.Occupied")}"),
                    new Part(empty, Grey, $"{N(empty)} {T("calc.Empty")}", $"{N(empty)} {T("calc.Empty")}")
                }, m.VehicleMiles is null ? "—" : N(m.VehicleMiles), scale: Scale(occupied + empty)));
            var shades = new[] { Violet, Deep, "#311B92" };
            string Riders(int n) => n == 1 ? T("calc.Riders1") : F("calc.RidersN", n);
            var patientParts = byRiders.Count > 0 && !m.Incomplete
                ? byRiders.Select((g, i) => new Part(g.Value, shades[Math.Min(i, shades.Length - 1)], $"{N(g.Value)} {Riders(g.Riders)}", $"{N(g.Value)} {Riders(g.Riders)}"))
                : new[] { new Part(m.PatientMiles ?? 0, Violet, N(m.PatientMiles)) };
            bars.Children.Add(Row(T("calc.Patient"), patientParts, N(m.PatientMiles), Violet, scale: Scale(m.PatientMiles ?? 0)));
            bars.Children.Add(Row(T("calc.Gps"), new[] { new Part(m.GpsMiles ?? 0, "#0EA5E9") }, N(m.GpsMiles), Mute, scale: Scale(m.GpsMiles ?? 0)));
            bars.Children.Add(Row(T("calc.Planned"), new[] { new Part(m.PlannedMiles ?? 0, "#64748B") }, N(m.PlannedMiles), Mute, scale: Scale(m.PlannedMiles ?? 0)));
            bars.Children.Add(Text(T("calc.PatientRule"), 12.5, Deep, true, TextAlignment.Center, new Thickness(0, 8, 0, 0)));
            _body.Children.Add(Card(bars));

            if (m.OdometerOut is long o && m.OdometerIn is long i2 && m.VehicleMiles is not null)
            {
                var odo = new StackPanel();
                odo.Children.Add(Equation(("", WarningLook.N((long?)i2), "Pull-in", Ink), ("−", WarningLook.N((long?)o), "Pull-out", Ink), ("=", N(m.VehicleMiles), T("calc.Odometer"), Violet)));
                odo.Children.Add(Equation(("", N(m.VehicleMiles), T("calc.Odometer"), Ink), ("−", N(occupied), T("calc.Occupied"), Ink), ("=", N(empty), T("calc.Empty"), Violet)));
                odo.Children.Add(Text(T("calc.EmptyRule"), 12, Mute, false, TextAlignment.Center, new Thickness(0, 4, 0, 0)));
                _body.Children.Add(Card(odo, new Thickness(0, 12, 0, 0)));
            }
            else _body.Children.Add(Card(Text(T("calc.NoOdometer"), 13, "#B3261E", true, TextAlignment.Center), new Thickness(0, 12, 0, 0)));

            var legs = new StackPanel();
            legs.Children.Add(Heading(T("calc.Legs")));
            if (m.Incomplete) legs.Children.Add(Text(T("calc.Incomplete"), 12, "#C2410C", false, TextAlignment.Center, new Thickness(0, 0, 0, 8)));
            legs.Children.Add(LegTable(m));
            _body.Children.Add(Card(legs, new Thickness(0, 12, 0, 0)));

            Pay(row, DriverPayRateType.PerMile, N(m.PatientMiles) + " mi", T("calc.Patient"), "info.Ex.NotMile");
        }

        private static Grid LegTable(MileCalculationDto m)
        {
            var g = new Grid();
            foreach (var w in new[] { new GridLength(30), new GridLength(98), new GridLength(1, GridUnitType.Star), new GridLength(80), new GridLength(84), new GridLength(104) })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

            void Put(int row, int col, string text, bool bold = false, string color = Ink, TextAlignment align = TextAlignment.Center, string? tip = null)
            {
                var t = Text(text, 12.5, color, bold, align, new Thickness(4, 5, 4, 5));
                t.ToolTip = tip;
                Grid.SetRow(t, row); Grid.SetColumn(t, col);
                g.Children.Add(t);
            }
            void NewRow(int row, string? back)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                if (back is null) return;
                var b = new Border { Background = WarningLook.Of(back), CornerRadius = new CornerRadius(5) };
                Grid.SetRow(b, row); Grid.SetColumnSpan(b, 6);
                g.Children.Add(b);
            }
            static string Place(string kind, string? trip) => kind switch
            {
                "Pickup" => $"{T("calc.Pickup")} {trip}", "Dropoff" => $"{T("calc.Dropoff")} {trip}", _ => kind
            };

            NewRow(0, "#EDE7F6");
            var heads = new[] { "#", T("calc.ColTime"), T("calc.ColLeg"), T("calc.ColMiles"), T("calc.ColPatients"), T("calc.ColPatientMiles") };
            for (var c = 0; c < heads.Length; c++) Put(0, c, heads[c], true, Deep);

            var r = 1;
            foreach (var l in m.Legs)
            {
                NewRow(r, r % 2 == 0 ? "#F6F8FA" : null);
                Put(r, 0, $"{r}", false, Mute);
                Put(r, 1, $"{WarningLook.Hour(l.FromUtc)} – {WarningLook.Hour(l.ToUtc)}", false, Mute);
                Put(r, 2, $"{Place(l.From, l.FromTrip)} → {Place(l.To, l.ToTrip)}");
                Put(r, 3, l.Miles.ToString("N2") + (l.Planned ? " *" : ""), false, Ink, TextAlignment.Center, l.Planned ? T("calc.PlannedMark") : null);
                Put(r, 4, $"× {l.Patients}", l.Patients > 0, l.Patients > 1 ? Deep : Ink);
                Put(r, 5, l.PatientMiles.ToString("N2"), l.Patients > 0, l.Patients > 0 ? Violet : Mute);
                r++;
            }
            NewRow(r, "#EDE7F6");
            Put(r, 2, T("calc.Total"), true, Deep);
            Put(r, 3, m.Legs.Sum(l => l.Miles).ToString("N2"), true, Deep);
            Put(r, 5, m.Legs.Sum(l => l.PatientMiles).ToString("N2"), true, Deep);
            if (m.Legs.Any(l => l.Planned))
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var note = Text("* " + T("calc.PlannedMark"), 11.5, Mute, false, TextAlignment.Center, new Thickness(0, 5, 0, 0));
                Grid.SetRow(note, r + 1); Grid.SetColumnSpan(note, 6);
                g.Children.Add(note);
            }
            return g;
        }

        /// <summary>Only for the administrator, who is the one the server sends rates to.</summary>
        private void Pay(ProductivityRow row, DriverPayRateType type, string units, string unitsLabel, string notKey)
        {
            if (!TripGuard.IsAdmin) return;
            var pay = new StackPanel();
            pay.Children.Add(Heading(T("calc.Pay")));
            if (row.Data.PayRateType is null) pay.Children.Add(Text(T("calc.NoRate"), 13, Mute, false, TextAlignment.Center));
            else if (row.Data.PayRateType == type)
                pay.Children.Add(Equation(("", units, unitsLabel, Violet), ("×", ProductivityFormat.Money(row.Data.PayRate), T("productivity.ColRate"), Ink), ("=", row.AmountText, T("productivity.ColAmount"), Green)));
            else pay.Children.Add(Text(T(notKey), 13, Mute, false, TextAlignment.Center));
            _body.Children.Add(Card(pay, new Thickness(0, 12, 0, 0)));
        }
    }
}
