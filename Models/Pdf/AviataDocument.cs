using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Raphael.Desktop.Models.Pdf
{
    public class AviataDocument : IDocument
    {
        private readonly List<IGrouping<string, ProductionReportRowDto>> _data;

        public AviataDocument(List<IGrouping<string, ProductionReportRowDto>> data) => _data = data;

        public void Compose(IDocumentContainer container)
        {
            foreach (var clientGroup in _data)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(8).FontFamily("Arial"));

                    page.Header().Element(h => ComposeHeader(h, clientGroup.First()));
                    page.Content().Element(c => ComposeContent(c, clientGroup));
                    page.Footer().PaddingTop(5).Element(f => {
                        f.AlignRight().Text(x => {
                            x.Span("Page ").FontSize(7);
                            x.CurrentPageNumber().FontSize(7);
                        });
                    });
                });
            }
        }

        private void ComposeHeader(IContainer container, ProductionReportRowDto first)
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("Client Name / DOB").Bold().Underline();
                    // Añadimos un PaddingTop para separar la etiqueta del valor
                    col.Item().PaddingTop(5).Text($"{first.Patient} / {first.DOB?.ToString("MM-dd-yyyy")}").Bold().FontSize(10);
                    col.Item().Text(first.PatientAddress);
                });

                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().Text("Funding Source / Insurance Id").Bold().Underline();
                    // Añadimos un PaddingTop aquí también
                    col.Item().PaddingTop(5).Text(first.FundingSource ?? "N/A").Bold().FontSize(10);
                });
            });
        }

        private void ComposeContent(IContainer container, IGrouping<string, ProductionReportRowDto> group)
        {
            container.PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(50); // Date
                    columns.ConstantColumn(50); // Run
                    columns.RelativeColumn(2);  // Pickup
                    columns.RelativeColumn(2);  // Dropoff
                    columns.ConstantColumn(40); // Num Spaces
                    columns.ConstantColumn(40); // Miles (Columna 6)
                    columns.RelativeColumn(1.5f); // Charge Name
                    columns.ConstantColumn(40); // Quantity
                    columns.ConstantColumn(40); // Rate
                    columns.ConstantColumn(50); // Ext Amount (Columna 10)
                    columns.RelativeColumn(1.5f); // Auth
                });

                // Header de la tabla...
                table.Header(header =>
                {
                    string[] titles = { "Date", "Run", "Pickup Address", "Dropoff Address", "Num Spaces", "Miles", "Charge Name", "Quantity", "$ Rate", "$ Ext Amount", "Authorization / Passenger Id" };
                    foreach (var title in titles)
                        header.Cell().Element(HeaderStyle).Text(title);
                });

                // Filas de datos...
                foreach (var trip in group)
                {
                    var bgColor = trip.Canceled ? "#FFB38A" : "#FFFFFF";

                    table.Cell().Background(bgColor).Element(CellStyle).Text(trip.Date.ToString("MM/dd/yy"));
                    table.Cell().Background(bgColor).Element(CellStyle).Text(trip.Run);
                    table.Cell().Background(bgColor).Element(CellStyle).Text(trip.PickupAddress).FontSize(7);
                    table.Cell().Background(bgColor).Element(CellStyle).Text(trip.DropoffAddress).FontSize(7);
                    table.Cell().Background(bgColor).Element(CellStyle).AlignCenter().Text("1");

                    // Columna Miles
                    table.Cell().Background(bgColor).Element(CellStyle).AlignCenter().Text((trip.Distance ?? 0.0).ToString("N2"));

                    table.Cell().Background(bgColor).Element(CellStyle).Column(c => {
                        foreach (var line in trip.BillableLines) c.Item().Text(line.ChargeName);
                    });
                    table.Cell().Background(bgColor).Element(CellStyle).Column(c => {
                        foreach (var line in trip.BillableLines) c.Item().AlignCenter().Text(line.Quantity.ToString("N1"));
                    });
                    table.Cell().Background(bgColor).Element(CellStyle).Column(c => {
                        foreach (var line in trip.BillableLines) c.Item().Text(line.Rate.ToString("C2"));
                    });

                    // Columna Ext Amount
                    table.Cell().Background(bgColor).Element(CellStyle).Column(c => {
                        foreach (var line in trip.BillableLines) c.Item().AlignRight().Text(line.Amount.ToString("C2"));
                    });

                    table.Cell().Background(bgColor).Element(CellStyle).Text(trip.Authorization);
                }

                // --- FILA DE TOTALES INTEGRADA PARA ALINEACIÓN PERFECTA ---
                // Combinamos las primeras 5 celdas para el texto "TOTALS"
                table.Cell().ColumnSpan(5).Background("#E0E0E0").Border(0.5f).Padding(2).Text("TOTALS:").Bold();

                // Total de Miles (Justo debajo de la columna Miles)
                table.Cell().Background("#D3D3D3").Border(0.5f).Padding(2).AlignCenter()
                     .Text((group.Sum(x => x.Distance) ?? 0.0).ToString("N2")).Bold();

                // Celdas vacías para Charge Name, Quantity y Rate (columnas 7, 8 y 9)
                table.Cell().ColumnSpan(3).Border(0.5f).Text("");

                // Total de Ext Amount (Justo debajo de la columna Ext Amount)
                table.Cell().Background("#D3D3D3").Border(0.5f).Padding(2).AlignRight()
                     .Text(group.Sum(x => x.TotalTripAmount).ToString("C2")).Bold();

                // Celda vacía final para Authorization
                table.Cell().Border(0.5f).Text("");
            });
        }

        static IContainer HeaderStyle(IContainer container) => container.Background("#D3D3D3").Border(0.5f).Padding(2).AlignCenter().AlignMiddle().DefaultTextStyle(x => x.Bold().FontSize(7));
        static IContainer CellStyle(IContainer container) => container.Border(0.5f).Padding(2).AlignMiddle();
    }
}