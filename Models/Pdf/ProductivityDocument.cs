using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Raphael.Desktop.Models.Pdf
{
    /// <summary>
    /// RE-081: the fleet productivity report as a PDF — one line per driver and day, and the period's totals.
    /// RE-082: with pay (administrators only), each driver's block ends in a subtotal and the table in the grand total.
    /// </summary>
    public class ProductivityDocument : IDocument
    {
        private readonly List<ProductivityReportRowDto> _rows;
        private readonly DateTime _start;
        private readonly DateTime _end;
        private readonly ProductivityTotals _totals;

        /// <param name="rows">Already in report order: driver, then day.</param>
        public ProductivityDocument(List<ProductivityReportRowDto> rows, DateTime start, DateTime end)
        {
            _rows = rows;
            _start = start;
            _end = end;
            _totals = ProductivityTotals.Of(rows);
        }

        private static string T(string key) => LocalizationService.Instance[key];

        public void Compose(IDocumentContainer container)
        {
            var pay = _totals.HasPay;
            var multiDriver = _rows.Select(r => r.DriverId).Distinct().Count() > 1;

            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(Fonts.SegoeUI));

                page.Header().Column(col =>
                {
                    col.Item().Text(T("productivity.Title")).FontSize(15).Bold().FontColor("#1E3A8A");
                    col.Item().Text($"{_start:MM/dd/yyyy} – {_end:MM/dd/yyyy}").FontSize(10);
                    col.Item().PaddingTop(6).Row(row =>
                    {
                        Tile(row, T("productivity.TotalDays"), _totals.Days.ToString());
                        Tile(row, T("productivity.TotalPaid"), _totals.PaidText);
                        Tile(row, T("productivity.TotalMiles"), _totals.MilesText);
                        Tile(row, T("productivity.TotalLoaded"), $"{_totals.PassengerMiles:N1} mi");
                        Tile(row, T("productivity.TotalTrips"), _totals.Trips.ToString());
                        if (pay) Tile(row, T("productivity.TotalAmount"), _totals.AmountText);
                    });
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    var keys = new List<string> { "ColDate", "ColDriver", "ColVehicle", "ColShift", "ColPaid", "HLoaded", "HWait", "HDeadhead",
                                                  "HInspection", "HOther", "ColMiles", "XPassenger", "MDeadhead", "ColTrips" };
                    if (pay) keys.AddRange(new[] { "ColRate", "ColAmount" });

                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(52); c.RelativeColumn(2.2f); c.RelativeColumn(2f); c.ConstantColumn(56);
                        c.ConstantColumn(38); c.ConstantColumn(42); c.ConstantColumn(40); c.ConstantColumn(44);
                        c.ConstantColumn(50); c.ConstantColumn(38); c.ConstantColumn(50); c.ConstantColumn(46); c.ConstantColumn(46); c.ConstantColumn(30);
                        if (pay) { c.ConstantColumn(48); c.ConstantColumn(54); }
                    });

                    table.Header(h =>
                    {
                        foreach (var key in keys)
                            h.Cell().Background("#1E3A8A").Padding(3).Text(T("productivity." + key)).FontColor(Colors.White).Bold();
                    });

                    void Sum(IReadOnlyCollection<ProductivityReportRowDto> set, string label, string bg, decimal? amount)
                    {
                        void C(string text, bool right = true) { var c = table.Cell().Background(bg).Padding(3); (right ? c.AlignRight() : c).Text(text).Bold(); }
                        // The label spans date, driver, vehicle and shift, so a long name stays on one line.
                        table.Cell().ColumnSpan(4).Background(bg).Padding(3).Text(label).Bold();
                        C(ProductivityFormat.Hours(set.Sum(r => r.PaidMinutes))); C(ProductivityFormat.Hours(set.Sum(r => r.LoadedMinutes)));
                        C(ProductivityFormat.Hours(set.Sum(r => r.ScheduleWaitMinutes + r.PickupWaitMinutes)));
                        C(ProductivityFormat.Hours(set.Sum(r => r.DeadheadDriveMinutes))); C(ProductivityFormat.Hours(set.Sum(r => r.InspectionMinutes)));
                        C(ProductivityFormat.Hours(set.Sum(r => r.OtherMinutes)));
                        C($"{set.Sum(r => r.VehicleMiles ?? 0):N1} mi"); C($"{set.Sum(r => r.PassengerMiles ?? 0):N1} mi");
                        C($"{set.Sum(r => r.DeadheadMiles ?? 0):N1} mi"); C(set.Sum(r => r.TripsCompleted).ToString());
                        if (pay) { C(""); C(ProductivityFormat.Money(amount)); }
                    }

                    var i = 0;
                    foreach (var block in _rows.GroupBy(r => r.DriverId))
                    {
                        var days = block.ToList();
                        foreach (var r in days)
                        {
                            var row = new ProductivityRow(r);
                            var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F1F4F7";
                            void Cell(string text, bool right = false)
                            {
                                var c = table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#DDE3EA").Padding(3);
                                (right ? c.AlignRight() : c).Text(text);
                            }
                            Cell($"{r.OperatingDate:MM/dd/yyyy}"); Cell(row.Driver); Cell(row.Vehicle); Cell(row.ShiftText);
                            Cell(row.PaidText, true); Cell(row.Loaded, true);
                            Cell(ProductivityFormat.Hours(r.ScheduleWaitMinutes + r.PickupWaitMinutes), true); Cell(row.DeadheadDrive, true);
                            Cell(row.Inspection, true); Cell(row.Other, true);
                            Cell(row.MilesText, true); Cell(row.PassengerMiles, true); Cell(row.DeadheadMiles, true); Cell(r.TripsCompleted.ToString(), true);
                            if (pay) { Cell(row.RateText, true); Cell(row.AmountText, true); }
                        }
                        // A subtotal only says something when there is more than one driver.
                        if (multiDriver)
                            Sum(days, $"{T("productivity.Subtotal")} · {days[0].DriverName ?? "—"}", "#EDE7F6", days.Sum(d => d.PayAmount ?? 0));
                    }

                    Sum(_rows, multiDriver && pay ? T("productivity.GrandTotal") : T("productivity.Totals"), "#E3E8EF", _totals.Amount);
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(T("productivity.PdfNote")).FontSize(7).FontColor("#5B6B7C");
                    row.ConstantItem(60).AlignRight().Text(x => { x.CurrentPageNumber(); x.Span(" / "); x.TotalPages(); });
                });
            });
        }

        private static void Tile(RowDescriptor row, string label, string value) =>
            row.RelativeItem().Border(0.5f).BorderColor("#DDE3EA").Padding(5).Column(c =>
            {
                c.Item().Text(label).FontSize(7).FontColor("#5B6B7C");
                c.Item().Text(value).FontSize(12).Bold();
            });
    }

    /// <summary>
    /// RE-081: the same report as a workbook, every figure in its own column, for payroll.
    /// RE-082: pay columns, a subtotal row per driver and a grand total, all as formulas.
    /// </summary>
    public static class ProductivityWorkbook
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public static void Save(List<ProductivityReportRowDto> rows, DateTime start, DateTime end, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(T("productivity.SheetName"));
            var pay = rows.Any(r => r.PayAmount is not null);
            var multiDriver = rows.Select(r => r.DriverId).Distinct().Count() > 1;

            var headers = new List<string>
            {
                "ColDate", "ColDriver", "ColVehicle", "XRoute", "XPaidStart", "XPaidEnd", "XPaidHours", "XShiftHours", "XTrimmed",
                "HLoaded", "HInspection", "XScheduleWait", "XPickupWait", "HDeadhead", "HOther", "XPatientHours",
                "XOdoOut", "XOdoIn", "ColMiles", "MLoaded", "MDeadhead", "XGps", "XPlanned", "XPassenger", "XOffRoute", "ColTrips", "XSuspect"
            };
            // 28 rate type, 29 rate, 30 units (hours or passenger miles), 31 amount.
            if (pay) headers.AddRange(new[] { "ColPayType", "ColRate", "ColUnits", "ColAmount" });

            for (var c = 0; c < headers.Count; c++) ws.Cell(1, c + 1).Value = T("productivity." + headers[c]);
            ws.Row(1).Style.Font.Bold = true;
            ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            ws.Row(1).Style.Font.FontColor = XLColor.White;

            // Hours as Excel durations (days), so payroll can add them up.
            static double H(int minutes) => minutes / 1440.0;
            static DateTime? L(DateTime? utc) => utc is { } u ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), TimeZoneInfo.Local) : null;

            var sumColumns = new List<int> { 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 19, 20, 21, 22, 23, 24, 25, 26, 27 };
            if (pay) sumColumns.Add(31);

            // SUBTOTAL(9, …) and not SUM: the grand total skips the drivers' subtotal rows instead of counting them twice.
            void Totals(int row, int from, int to, string label, string color)
            {
                ws.Cell(row, 1).Value = label;
                foreach (var c in sumColumns)
                    ws.Cell(row, c).FormulaA1 = $"SUBTOTAL(9,{ws.Cell(from, c).Address}:{ws.Cell(Math.Max(from, to), c).Address})";
                ws.Row(row).Style.Font.Bold = true;
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml(color);
            }

            var r = 2;
            foreach (var block in rows.GroupBy(x => x.DriverId))
            {
                var first = r;
                foreach (var d in block)
                {
                    var values = new List<object?>
                    {
                        d.OperatingDate, d.DriverName, d.VehicleName, d.RouteName, L(d.PaidStartUtc), L(d.PaidEndUtc), H(d.PaidMinutes), H(d.ShiftMinutes),
                        H(d.StartTrimmedMinutes + d.EndTrimmedMinutes), H(d.LoadedMinutes), H(d.InspectionMinutes), H(d.ScheduleWaitMinutes),
                        H(d.PickupWaitMinutes), H(d.DeadheadDriveMinutes), H(d.OtherMinutes), H(d.PatientMinutes),
                        d.OdometerOut, d.OdometerIn, d.VehicleMiles, d.LoadedMiles, d.DeadheadMiles, d.GpsMiles, d.PlannedMiles, d.PassengerMiles,
                        d.OffRouteMiles, d.TripsCompleted, d.SuspectMarks
                    };
                    if (pay)
                    {
                        values.Add(d.PayRateType is null ? null : T(d.PayRateType == DriverPayRateType.Hourly ? "pay.Hourly" : "pay.PerMile"));
                        values.Add(d.PayRate);
                        values.Add(d.PayUnits);
                    }
                    for (var c = 0; c < values.Count; c++) ws.Cell(r, c + 1).Value = XLCellValue.FromObject(values[c]);
                    // The amount as rate × units, so a corrected rate recalculates; rounded to the cent like the server.
                    if (pay && d.PayRate is not null && d.PayUnits is not null)
                        ws.Cell(r, 31).FormulaA1 = $"ROUND({ws.Cell(r, 29).Address}*{ws.Cell(r, 30).Address},2)";
                    r++;
                }
                if (multiDriver)
                {
                    Totals(r, first, r - 1, $"{T("productivity.Subtotal")} · {block.First().DriverName ?? "—"}", "#EDE7F6");
                    r++;
                }
            }

            ws.Column(1).Style.DateFormat.Format = "MM/dd/yyyy";
            ws.Columns(5, 6).Style.DateFormat.Format = "HH:mm";
            ws.Columns(7, 16).Style.NumberFormat.Format = "[h]:mm";
            ws.Columns(19, 24).Style.NumberFormat.Format = "#,##0.0";
            if (pay)
            {
                ws.Column(29).Style.NumberFormat.Format = "$#,##0.00";
                ws.Column(30).Style.NumberFormat.Format = "#,##0.00";
                ws.Column(31).Style.NumberFormat.Format = "$#,##0.00";
            }

            // Totals: formulas, so a filtered or edited sheet still adds up.
            Totals(r, 2, r - 1, multiDriver && pay ? T("productivity.GrandTotal") : T("productivity.Totals"), "#E3E8EF");

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
            wb.Properties.Title = $"{T("productivity.Title")} {start:yyyy-MM-dd} – {end:yyyy-MM-dd}";
            wb.SaveAs(path);
        }
    }
}
