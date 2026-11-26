#nullable enable
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Raphael.Desktop.Models.Pdf
{
    /// <summary>
    /// RE-096: the claim for one funding source. In English, because it goes to the funding source, and
    /// only with its own trips. The patient's name is left out unless somebody asks for it.
    /// </summary>
    public class ImpossibleAppointmentsDocument : IDocument
    {
        private readonly List<ImpossibleAppointmentDto> _trips;
        private readonly ImpossibleFundingSourceDto _source;
        private readonly DateTime _from, _to;
        private readonly bool _names;

        public ImpossibleAppointmentsDocument(ImpossibleFundingSourceDto source, List<ImpossibleAppointmentDto> trips, DateTime from, DateTime to, bool names)
        {
            _source = source;
            _trips = trips;
            _from = from;
            _to = to;
            _names = names;
        }

        public static string VerdictText(ImpossibleAppointmentDto t) => t.Verdict switch
        {
            "FACTS" => "Proven by facts: the driver was at the pickup on time and the appointment was still missed.",
            "CALCULATION" => t.Check.Kind == FeasibilityText.BadHours
                ? "Proven by the hours received: the appointment is not after the pickup."
                : "Proven by calculation: the time received is shorter than boarding plus the drive.",
            "LOOSE" => "Loose: more than 30 minutes to spare.",
            "MANUAL" => "Not claimed: typed in the office, not sent by the funding source.",
            "DO_NOT_CLAIM" => "Not claimed: the pickup was late as well.",
            _ => "Arguable: five minutes short or less, or the drive was not confirmed.",
        };

        public static string SourceText(string s) => s switch { "INTEGRATION" => "Integration", "FILE" => "Funding source file", "PROVIDER" => "Provider", "MANUAL" => "Typed in the office", _ => "Unknown" };

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(1.5f, QuestPDF.Infrastructure.Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.SegoeUI));

                page.Header().Column(col =>
                {
                    col.Item().Text("Appointments that could not be reached on time").FontSize(16).Bold().FontColor("#4527A0");
                    col.Item().Text($"{_source.FundingSource}  ·  {_from:MM/dd/yyyy} – {_to:MM/dd/yyyy}").FontSize(11);
                    col.Item().PaddingTop(4).LineHorizontal(1).LineColor("#D1C4E9");
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text(t =>
                    {
                        t.Span($"{_trips.Count}").Bold();
                        t.Span($" of {_source.Appointments} appointments in the period are listed here. ");
                        t.Span($"{_trips.Count(x => x.Verdict == "FACTS")}").Bold();
                        t.Span(" were missed although the driver reached the pickup on time.");
                    });
                    col.Item().Background("#F4F1FA").Padding(8).Column(m =>
                    {
                        m.Item().Text("Method").Bold();
                        m.Item().Text("Available time = appointment hour − pickup hour, both as received. Needed time = boarding + drive. The drive is Google's, with the traffic expected at that hour, rounded down to the minute, unless a trip says it is estimated. "
                            + "An appointment is impossible when a driver at the door at the promised hour cannot reach it. Actual hours are those recorded by the driver's app.");
                    });
                    foreach (var t in _trips) col.Item().ShowEntire().Element(c => Trip(c, t));
                });

                page.Footer().AlignCenter().Text(t => { t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages(); });
            });
        }

        private static string Late(ImpossibleAppointmentDto t) =>
            t.DropoffArrived is { } d && (t.Check.OriginalToTime ?? t.Check.ToTime) is { } to && (int)Math.Floor(d.TotalMinutes) - (int)Math.Floor(to.TotalMinutes) is var late && late > 0
                ? late > 15 ? $" ({late} min past the appointment, beyond the 15 min admitted)" : $" ({late} min past the appointment, within the 15 min admitted)" : "";

        private void Trip(IContainer container, ImpossibleAppointmentDto t)
        {
            var c = t.Check;
            container.Border(0.7f).BorderColor("#E3E8EF").Padding(8).Column(col =>
            {
                col.Spacing(2);
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text($"Trip {t.TripNumber}" + (string.IsNullOrWhiteSpace(t.Authorization) ? "" : $"  ·  Authorization {t.Authorization}")).Bold();
                    r.ConstantItem(150).AlignRight().Text($"{t.Date:MM/dd/yyyy}  ·  {t.SpaceType}");
                });
                if (_names && !string.IsNullOrWhiteSpace(t.Patient)) col.Item().Text($"Patient: {t.Patient}");
                col.Item().Text($"From {t.PickupAddress}");
                col.Item().Text($"To {t.DropoffAddress}" + (c.DriveMiles is { } mi ? $"  ({mi:0.0} miles)" : ""));
                col.Item().Text($"Received: {(t.CreatedUtc is { } utc ? utc.ToString("MM/dd/yyyy HH:mm") + " UTC" : "—")}, {SourceText(t.Source)}. Pickup {FeasibilityText.Hour(c.OriginalFromTime)}, appointment {FeasibilityText.Hour(c.OriginalToTime)}"
                    + (c.OriginalConfirmed ? "." : " (original hours not confirmed: the trip was first seen later)."));
                if (c.Adjustment is FeasibilityText.Adjusted or FeasibilityText.ByHand)
                    col.Item().Text($"We moved the pickup to {FeasibilityText.Hour(c.FromTime)} to reach the appointment.").FontColor("#2E7D32");
                if (c.Kind == FeasibilityText.BadHours)
                    col.Item().Text($"The appointment ({FeasibilityText.Hour(c.OriginalToTime ?? c.ToTime)}) is not after the pickup ({FeasibilityText.Hour(c.OriginalFromTime ?? c.FromTime)}).").FontColor("#B71C1C").Bold();
                else
                {
                    var needed = (c.DriveMinutes ?? 0) + c.BoardingMinutes + c.LeadMinutes;
                    col.Item().Text(x =>
                    {
                        x.Span($"Available {c.AvailableMinutes} min.  Needed: boarding {c.BoardingMinutes} + drive {c.DriveMinutes}" + (c.LeadMinutes > 0 ? $" + {c.LeadMinutes} before the appointment" : "") + $" = {needed} min.  ");
                        x.Span($"{c.MissingMinutes} min short.").FontColor("#B71C1C").Bold();
                    });
                    col.Item().Row(r =>
                    {
                        var total = Math.Max(needed, 1);
                        r.RelativeItem(Math.Max(1, c.AvailableMinutes ?? 1)).Height(7).Background("#7E57C2");
                        if (total > (c.AvailableMinutes ?? 0)) r.RelativeItem(total - (c.AvailableMinutes ?? 0)).Height(7).Background("#E53935");
                    });
                    col.Item().Text(c.DriveIsPriced ? $"Drive: Google Routes, consulted {(c.DrivePricedAtUtc is { } at ? at.ToString("MM/dd/yyyy HH:mm") + " UTC" : "—")}." : "Drive: estimated, not confirmed by Google.").FontSize(8).FontColor("#5B6B7C");
                    if (c.SuggestedFromTime is { } hour) col.Item().Text($"A pickup hour of {FeasibilityText.Hour(hour)} would have made it possible.").FontSize(8).FontColor("#5B6B7C");
                }
                if (t.PickupArrived != null || t.DropoffArrived != null)
                    col.Item().Text($"What happened: at the pickup {FeasibilityText.Hour(t.PickupArrived)}, on board {FeasibilityText.Hour(t.PickupPerformed)}, at the destination {FeasibilityText.Hour(t.DropoffArrived)}{Late(t)}. {t.Line}, {t.Vehicle}.");
                col.Item().Text(VerdictText(t)).Bold().FontColor("#4527A0");
            });
        }
    }

    /// <summary>RE-096: every figure of the report, a row per trip.</summary>
    public static class ImpossibleAppointmentsWorkbook
    {
        public static void Save(List<ImpossibleAppointmentDto> trips, List<ImpossibleFundingSourceDto> sources, DateTime from, DateTime to, bool names, string path)
        {
            using var book = new XLWorkbook();
            var s = book.AddWorksheet("Summary");
            s.Cell(1, 1).Value = $"Impossible appointments {from:MM/dd/yyyy} - {to:MM/dd/yyyy}";
            s.Cell(1, 1).Style.Font.Bold = true;
            string[] sh = { "Funding source", "Appointments", "Impossible", "Hours contradict", "Percent", "Claimable", "Arrived late", "Loose", "Adjusted" };
            for (var i = 0; i < sh.Length; i++) s.Cell(3, i + 1).Value = sh[i];
            var n = 4;
            foreach (var f in sources)
            {
                s.Cell(n, 1).Value = f.FundingSource;
                s.Cell(n, 2).Value = f.Appointments;
                s.Cell(n, 3).Value = f.Impossible;
                s.Cell(n, 4).Value = f.BadHours;
                s.Cell(n, 5).Value = f.Appointments == 0 ? 0 : (f.Impossible + f.BadHours) / (double)f.Appointments;
                s.Cell(n, 5).Style.NumberFormat.Format = "0.0%";
                s.Cell(n, 6).Value = f.Claimable;
                s.Cell(n, 7).Value = f.ArrivedLate;
                s.Cell(n, 8).Value = f.Loose;
                s.Cell(n, 9).Value = f.Adjusted;
                n++;
            }
            s.Row(3).Style.Font.Bold = true;
            s.Columns().AdjustToContents();

            var w = book.AddWorksheet("Trips");
            var heads = new List<string> { "Date", "Trip", "Authorization", "Funding source", "Space" };
            if (names) heads.Add("Patient");
            heads.AddRange(new[] { "Pickup address", "Dropoff address", "Received (UTC)", "Source", "Original pickup", "Original appointment", "Original confirmed", "Pickup", "Appointment",
                "Available min", "Boarding min", "Drive min", "Drive miles", "Google", "Lead min", "Missing min", "Suggested pickup", "Kind", "Adjustment", "Adjusted pickup", "Adjusted by", "Adjusted at (UTC)", "Patient to be told", "Line", "Driver", "Vehicle", "Arrived pickup", "On board", "Arrived destination", "Pickup GPS", "Dropoff GPS", "Verdict" });
            for (var i = 0; i < heads.Count; i++) w.Cell(1, i + 1).Value = heads[i];
            w.Row(1).Style.Font.Bold = true;
            var row = 2;
            foreach (var t in trips)
            {
                var c = t.Check;
                var col = 1;
                void Put(XLCellValue v) => w.Cell(row, col++).Value = v;
                Put(t.Date.ToString("MM/dd/yyyy")); Put(t.TripNumber ?? ""); Put(t.Authorization ?? ""); Put(t.FundingSource ?? ""); Put(t.SpaceType ?? "");
                if (names) Put(t.Patient ?? "");
                Put(t.PickupAddress); Put(t.DropoffAddress); Put(t.CreatedUtc?.ToString("MM/dd/yyyy HH:mm") ?? ""); Put(ImpossibleAppointmentsDocument.SourceText(t.Source));
                Put(FeasibilityText.Hour(c.OriginalFromTime)); Put(FeasibilityText.Hour(c.OriginalToTime)); Put(c.OriginalConfirmed ? "Yes" : "No");
                Put(FeasibilityText.Hour(c.FromTime)); Put(FeasibilityText.Hour(c.ToTime));
                Put(c.AvailableMinutes ?? 0); Put(c.BoardingMinutes); Put(c.DriveMinutes ?? 0); Put(Math.Round(c.DriveMiles ?? 0, 1)); Put(c.DriveIsPriced ? "Yes" : "No"); Put(c.LeadMinutes); Put(c.MissingMinutes);
                Put(c.SuggestedFromTime is null ? "" : FeasibilityText.Hour(c.SuggestedFromTime)); Put(c.Kind == FeasibilityText.Loose ? "Loose" : c.Kind == FeasibilityText.BadHours ? "Hours contradict" : "Impossible");
                Put(c.Adjustment switch { 1 => "Adjusted", 2 => "Left as received", 3 => "Put back", 4 => "Changed by hand", _ => "Not adjusted" });
                Put(c.AdjustedFromTime is null ? "" : FeasibilityText.Hour(c.AdjustedFromTime)); Put(c.AdjustedBy ?? ""); Put(c.AdjustedAtUtc?.ToString("MM/dd/yyyy HH:mm") ?? ""); Put(c.PatientNotice ? "Yes" : "");
                Put(t.Line ?? ""); Put(t.Driver ?? ""); Put(t.Vehicle ?? "");
                Put(t.PickupArrived is null ? "" : FeasibilityText.Hour(t.PickupArrived)); Put(t.PickupPerformed is null ? "" : FeasibilityText.Hour(t.PickupPerformed)); Put(t.DropoffArrived is null ? "" : FeasibilityText.Hour(t.DropoffArrived));
                Put(t.PickupGps ?? ""); Put(t.DropoffGps ?? ""); Put(ImpossibleAppointmentsDocument.VerdictText(t));
                row++;
            }
            w.SheetView.FreezeRows(1);
            w.Columns().AdjustToContents(1, Math.Min(row, 60));
            book.SaveAs(path);
        }
    }
}
