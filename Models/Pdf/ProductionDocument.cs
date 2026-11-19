using Raphael.Desktop.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.Models.Pdf
{
    public class ProductionDocument : IDocument
    {
        private readonly List<ProductionReportRowDto> _data;
        private readonly DateTime _startDate;
        private readonly DateTime _endDate;
        private readonly bool _cancelledIncluded;
        private readonly ProductionTotals _totals;

        public ProductionDocument(List<ProductionReportRowDto> data, DateTime startDate, DateTime endDate, bool cancelledIncluded = true)
        {
            _data = data;
            _startDate = startDate;
            _endDate = endDate;
            _cancelledIncluded = cancelledIncluded;
            _totals = ProductionTotals.Of(data);
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(8).FontFamily(Fonts.SegoeUI));

                page.Header().Element(ComposeHeader);
                page.Content().Element(ComposeContent);
                page.Footer().AlignCenter().Text(x => {
                    x.Span("Page "); x.CurrentPageNumber();
                });
            });
        }

        void ComposeHeader(IContainer container)
        {
            container.Row(row => {
                row.RelativeItem().Column(col => {
                    col.Item().Text("PRODUCTION REPORT").FontSize(16).SemiBold().FontColor("#007ACC");
                    col.Item().Text($"From {_startDate:MM/dd/yyyy} to {_endDate:MM/dd/yyyy}" + (_cancelledIncluded ? "" : "  (cancelled trips not included)"));
                    // RE-076: the totals on every page, next to the period they belong to.
                    col.Item().Text(_totals.Summary).SemiBold();
                });
                row.RelativeItem().AlignRight().Text(DateTime.Now.ToString("g"));
            });
        }

        void ComposeContent(IContainer container)
        {
            container.PaddingTop(10).Column(col =>
            {
                foreach (var item in _data)
                {
                    col.Item().PaddingBottom(15).Border(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5).Column(tripCol =>
                    {
                        // CABECERA DEL VIAJE
                        tripCol.Item().Background(Colors.Grey.Lighten4).Padding(2).Row(r => {
                            r.RelativeItem().Text($"TRIP ID: {item.TripId}").Bold();
                            r.RelativeItem().AlignCenter().Text($"DATE: {item.Date:MM/dd/yyyy}").Bold();
                            r.RelativeItem().AlignRight().Text(item.Canceled ? "CANCELED" : "ACTIVE").FontColor(item.Canceled ? Colors.Red.Medium : Colors.Green.Medium).Bold();
                        });

                        // GRID DE DATOS (Mapeo de los 48 campos del Excel)
                        tripCol.Item().Grid(grid =>
                        {
                            grid.VerticalSpacing(2);
                            grid.HorizontalSpacing(5);
                            grid.Columns(4); // 4 columnas de etiquetas/valores

                            // --- SECCIÓN 1: TIEMPOS ---
                            AddEntry(grid, "Req Pickup", item.ReqPickup?.ToString(@"hh\:mm"));
                            AddEntry(grid, "Appointment", item.Appointment?.ToString(@"hh\:mm"));
                            AddEntry(grid, "PU Arrive", item.PickupArrive?.ToString(@"hh\:mm"));
                            AddEntry(grid, "PU Perform", item.PickupPerform?.ToString(@"hh\:mm"));
                            AddEntry(grid, "DO Arrive", item.DropoffArrive?.ToString(@"hh\:mm"));
                            AddEntry(grid, "DO Perform", item.DropoffPerform?.ToString(@"hh\:mm"));
                            AddEntry(grid, "Will Call Time", item.WillCallTime?.ToString(@"hh\:mm"));
                            AddEntry(grid, "Will Call", item.WillCall ? "Yes" : "No");

                            // --- SECCIÓN 2: PACIENTE ---
                            AddEntry(grid, "Patient", item.Patient);
                            AddEntry(grid, "DOB", item.DOB?.ToShortDateString());
                            AddEntry(grid, "PU Phone", item.PickupPhone);
                            AddEntry(grid, "DO Phone", item.DropoffPhone);
                            AddEntry(grid, "Patient Addr", item.PatientAddress);

                            // --- SECCIÓN 3: UBICACIONES PU ---
                            AddEntry(grid, "PU Address", item.PickupAddress);
                            AddEntry(grid, "PU City", item.PickupCity);
                            AddEntry(grid, "PU State", item.PickupState);
                            AddEntry(grid, "PU Zip", item.PickupZip);

                            // --- SECCIÓN 4: UBICACIONES DO ---
                            AddEntry(grid, "DO Address", item.DropoffAddress);
                            AddEntry(grid, "DO City", item.DropoffCity);
                            AddEntry(grid, "DO State", item.DropoffState);
                            AddEntry(grid, "DO Zip", item.DropoffZip);

                            // --- SECCIÓN 5: LOGÍSTICA Y COBRO ---
                            AddEntry(grid, "Funding Src", item.FundingSource);
                            AddEntry(grid, "Authorization", item.Authorization);
                            AddEntry(grid, "Charge", (item.Charge ?? 0).ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture));
                            AddEntry(grid, "Paid", (item.Paid ?? 0).ToString("$#,##0.00", System.Globalization.CultureInfo.InvariantCulture));
                            AddEntry(grid, "Space", item.Space);
                            AddEntry(grid, "Type", item.Type);
                            AddEntry(grid, "Distance", item.Distance?.ToString());
                            AddEntry(grid, "Run", item.Run);

                            // --- SECCIÓN 6: VEHÍCULO Y CONDUCTOR ---
                            AddEntry(grid, "Driver", item.Driver);
                            AddEntry(grid, "Vehicle", item.Vehicle);
                            AddEntry(grid, "Plate", item.VehiclePlate);
                            AddEntry(grid, "VIN", item.VIN);
                            AddEntry(grid, "PU Odometer", item.PickupOdometer?.ToString());
                            AddEntry(grid, "DO Odometer", item.DropoffOdometer?.ToString());
                            AddEntry(grid, "No-Show Reas", item.DriverNoShowReason);

                            // --- SECCIÓN 7: COMENTARIOS ---
                            grid.Item(2).Text(x => { x.Span("PU Comment: ").Bold(); x.Span(item.PickupComment); });
                            grid.Item(2).Text(x => { x.Span("DO Comment: ").Bold(); x.Span(item.DropoffComment); });

                            // --- SECCIÓN 8: TÉCNICO / GPS ---
                            AddEntry(grid, "PU GPS Dist", item.PickupGpsArriveDistance?.ToString());
                            AddEntry(grid, "DO GPS Dist", item.DropoffGpsArriveDistance?.ToString());
                            AddEntry(grid, "PU Lat", item.PickupLat.ToString());
                            AddEntry(grid, "PU Lon", item.PickupLon.ToString());
                            AddEntry(grid, "DO Lat", item.DropoffLat.ToString());
                            AddEntry(grid, "DO Lon", item.DropoffLon.ToString());
                            AddEntry(grid, "Created", item.Created.ToString("g"));
                        });
                    });
                }
            });
        }

        // Función auxiliar para añadir etiquetas y valores al grid
        void AddEntry(GridDescriptor grid, string label, string value)
        {
            grid.Item(1).Text(x => {
                x.Span($"{label}: ").Bold().FontSize(7);
                x.Span(value ?? "N/A").FontSize(7);
            });
        }
    }

    /// <summary>The totals of a production report (RE-076), the same in the preview and in the Excel file.</summary>
    public sealed class ProductionTotals
    {
        public int Trips { get; init; }
        public int Billable { get; init; }
        public int Cancelled { get; init; }
        public double Miles { get; init; }
        public decimal Charge { get; init; }

        public static ProductionTotals Of(IReadOnlyCollection<ProductionReportRowDto> rows) => new()
        {
            Trips = rows.Count,
            Cancelled = rows.Count(r => r.Canceled),
            Billable = rows.Count(r => !r.Canceled),
            Miles = rows.Where(r => !r.Canceled).Sum(r => r.Distance ?? 0),
            Charge = rows.Sum(r => (decimal)(r.Charge ?? 0)),
        };

        public string Summary =>
            FormattableString.Invariant($"Trips: {Trips}   Billable: {Billable}   Cancelled: {Cancelled}   Miles: {Miles:N1}   Total charge: ${Charge:N2}");
    }
}
