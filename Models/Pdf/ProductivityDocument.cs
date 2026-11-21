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
    /// <summary>RE-081: the fleet productivity report as a PDF — one line per driver and day, and the period's totals.</summary>
    public class ProductivityDocument : IDocument
    {
        private readonly List<ProductivityReportRowDto> _rows;
        private readonly DateTime _start;
        private readonly DateTime _end;
        private readonly ProductivityTotals _totals;

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
                        Tile(row, T("productivity.TotalLoaded"), _totals.LoadedText);
                        Tile(row, T("productivity.TotalTrips"), _totals.Trips.ToString());
                    });
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(58); c.RelativeColumn(2.2f); c.RelativeColumn(2f); c.ConstantColumn(62);
                        c.ConstantColumn(40); c.ConstantColumn(44); c.ConstantColumn(42); c.ConstantColumn(46);
                        c.ConstantColumn(54); c.ConstantColumn(40); c.ConstantColumn(52); c.ConstantColumn(48); c.ConstantColumn(48); c.ConstantColumn(32);
                    });

                    table.Header(h =>
                    {
                        foreach (var key in new[] { "ColDate", "ColDriver", "ColVehicle", "ColShift", "ColPaid", "HLoaded", "HWait", "HDeadhead",
                                                    "HInspection", "HOther", "ColMiles", "MLoaded", "MDeadhead", "ColTrips" })
                            h.Cell().Background("#1E3A8A").Padding(3).Text(T("productivity." + key)).FontColor(Colors.White).Bold();
                    });

                    var i = 0;
                    foreach (var r in _rows)
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
                        Cell(row.MilesText, true); Cell(row.LoadedMiles, true); Cell(row.DeadheadMiles, true); Cell(r.TripsCompleted.ToString(), true);
                    }

                    void Total(string text, bool right = true) { var c = table.Cell().Background("#E3E8EF").Padding(3); (right ? c.AlignRight() : c).Text(text).Bold(); }
                    Total(T("productivity.Totals"), false); Total(""); Total(""); Total("");
                    Total(_totals.PaidText); Total(ProductivityFormat.Hours(_rows.Sum(r => r.LoadedMinutes)));
                    Total(ProductivityFormat.Hours(_rows.Sum(r => r.ScheduleWaitMinutes + r.PickupWaitMinutes)));
                    Total(ProductivityFormat.Hours(_rows.Sum(r => r.DeadheadDriveMinutes))); Total(ProductivityFormat.Hours(_rows.Sum(r => r.InspectionMinutes)));
                    Total(ProductivityFormat.Hours(_rows.Sum(r => r.OtherMinutes)));
                    Total(_totals.MilesText); Total($"{_totals.LoadedMiles:N1} mi"); Total($"{_totals.DeadheadMiles:N1} mi"); Total(_totals.Trips.ToString());
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

    /// <summary>RE-081: the same report as a workbook, every figure in its own column, for payroll.</summary>
    public static class ProductivityWorkbook
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public static void Save(List<ProductivityReportRowDto> rows, DateTime start, DateTime end, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(T("productivity.SheetName"));

            var headers = new[]
            {
                "ColDate", "ColDriver", "ColVehicle", "XRoute", "XPaidStart", "XPaidEnd", "XPaidHours", "XShiftHours", "XTrimmed",
                "HLoaded", "HInspection", "XScheduleWait", "XPickupWait", "HDeadhead", "HOther", "XPatientHours",
                "XOdoOut", "XOdoIn", "ColMiles", "MLoaded", "MDeadhead", "XGps", "XPlanned", "XPassenger", "XOffRoute", "ColTrips", "XSuspect"
            };
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = T("productivity." + headers[c]);
            ws.Row(1).Style.Font.Bold = true;
            ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A8A");
            ws.Row(1).Style.Font.FontColor = XLColor.White;

            // Hours as Excel durations (days), so payroll can add them up.
            static double H(int minutes) => minutes / 1440.0;
            static DateTime? L(DateTime? utc) => utc is { } u ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(u, DateTimeKind.Utc), TimeZoneInfo.Local) : null;

            var r = 2;
            foreach (var d in rows)
            {
                var values = new object?[]
                {
                    d.OperatingDate, d.DriverName, d.VehicleName, d.RouteName, L(d.PaidStartUtc), L(d.PaidEndUtc), H(d.PaidMinutes), H(d.ShiftMinutes),
                    H(d.StartTrimmedMinutes + d.EndTrimmedMinutes), H(d.LoadedMinutes), H(d.InspectionMinutes), H(d.ScheduleWaitMinutes),
                    H(d.PickupWaitMinutes), H(d.DeadheadDriveMinutes), H(d.OtherMinutes), H(d.PatientMinutes),
                    d.OdometerOut, d.OdometerIn, d.VehicleMiles, d.LoadedMiles, d.DeadheadMiles, d.GpsMiles, d.PlannedMiles, d.PassengerMiles,
                    d.OffRouteMiles, d.TripsCompleted, d.SuspectMarks
                };
                for (var c = 0; c < values.Length; c++) ws.Cell(r, c + 1).Value = XLCellValue.FromObject(values[c]);
                r++;
            }

            ws.Column(1).Style.DateFormat.Format = "MM/dd/yyyy";
            ws.Columns(5, 6).Style.DateFormat.Format = "HH:mm";
            ws.Columns(7, 16).Style.NumberFormat.Format = "[h]:mm";
            ws.Columns(19, 24).Style.NumberFormat.Format = "#,##0.0";

            // Totals: formulas, so a filtered or edited sheet still adds up.
            ws.Cell(r, 1).Value = T("productivity.Totals");
            foreach (var c in new[] { 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 19, 20, 21, 22, 23, 24, 25, 26, 27 })
                ws.Cell(r, c).FormulaA1 = $"SUM({ws.Cell(2, c).Address}:{ws.Cell(Math.Max(2, r - 1), c).Address})";
            ws.Row(r).Style.Font.Bold = true;
            ws.Row(r).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3E8EF");

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
            wb.Properties.Title = $"{T("productivity.Title")} {start:yyyy-MM-dd} – {end:yyyy-MM-dd}";
            wb.SaveAs(path);
        }
    }
}
