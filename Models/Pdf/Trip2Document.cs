using System.Text;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Raphael.Desktop.DTOs;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Raphael.Desktop.Models.Pdf
{
    public class Trip2Document : IDocument
    {
        private List<IGrouping<string, ProductionReportRowDto>> _data;
        private DateTime _date;

        public Trip2Document(List<IGrouping<string, ProductionReportRowDto>> data, DateTime date)
        {
            _data = data;
            _date = date;
        }

        public void Compose(IDocumentContainer container)
        {
            // Generamos una sección de página por cada Driver
            foreach (var driverGroup in _data)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, QuestPDF.Infrastructure.Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Element(header => ComposeHeader(header, driverGroup));
                    page.Content().Element(content => ComposeContent(content, driverGroup));
                    page.Footer().Element(ComposeFooter);
                });
            }
        }

        private void ComposeHeader(IContainer container, IGrouping<string, ProductionReportRowDto> group)
        {
            var firstRow = group.First();

            container.Column(column =>
            {
                column.Item().AlignCenter().Text("DAILY TRIP LOG").Bold().FontSize(14);

                column.Item().PaddingTop(10).Row(row =>
                {
                    // Provider Section
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("Milanes Transport LLC");
                        c.Item().BorderBottom(1).PaddingBottom(2);
                        c.Item().Text("Provider Name:").FontSize(7);
                    });

                    row.ConstantItem(40); // Espacio aumentado para evitar colisiones

                    // Date Section
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(_date.ToString("MM/dd/yyyy"));
                        c.Item().BorderBottom(1).PaddingBottom(2);
                        c.Item().Text("Last Day Billed").FontSize(7);
                    });
                });

                column.Item().PaddingTop(5).Row(row =>
                {
                    // Driver Section
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(group.Key.ToUpper());
                        c.Item().BorderBottom(1).PaddingBottom(2);
                        c.Item().Text("DRIVER'S NAME (as it appears on driver license)").FontSize(7);
                    });

                    row.ConstantItem(40);

                    // Vehicle Section
                    row.RelativeItem().Column(c =>
                    {
                        string vin = firstRow.VIN ?? "";
                        string last6 = vin.Length >= 6 ? vin.Substring(vin.Length - 6) : vin;
                        c.Item().Text(last6);
                        c.Item().BorderBottom(1).PaddingBottom(2);
                        c.Item().Text("Vehicle Number (Last six of VIN)").FontSize(7);
                    });
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
                    columns.ConstantColumn(60); // Job#
                    columns.RelativeColumn(2);  // Name
                    columns.ConstantColumn(25); // AWS
                    columns.ConstantColumn(45); // Pickup
                    columns.ConstantColumn(45); // Dropoff
                    columns.ConstantColumn(45); // Will Call
                    columns.ConstantColumn(45); // Mileage
                    columns.ConstantColumn(45); // Amount
                    columns.RelativeColumn(2);  // Signature
                });

                // Table Header
                table.Header(header =>
                {
                    string[] headers = { "Date of Service", "ModivCare Job# A or B", "Recipient's Name", "A W S", "Pick-up Time", "Drop-Off Time", "Will Call Time", "Total Trip Mileage", "Per Trip Billed Amount", "Recipient's Signature" };
                    foreach (var h in headers)
                    {
                        header.Cell().Border(0.5f).Padding(2).AlignCenter().Text(h).Bold().FontSize(7);
                    }
                });

                // Table Rows
                foreach (var item in group)
                {
                    table.Cell().Element(CellStyle).Text(item.Date.ToString("M/d/yyyy"));
                    table.Cell().Element(CellStyle).Text(item.TripId);
                    table.Cell().Element(CellStyle).Text(item.Patient);

                    // Lógica AWS segura
                    string awsInit = !string.IsNullOrEmpty(item.Space) ? item.Space.Substring(0, 1).ToUpper() : "A";
                    table.Cell().Element(CellStyle).AlignCenter().Text(awsInit);

                    table.Cell().Element(CellStyle).AlignCenter().Text(item.PickupPerform?.ToString(@"hh\:mm") ?? "");
                    table.Cell().Element(CellStyle).AlignCenter().Text(item.DropoffPerform?.ToString(@"hh\:mm") ?? "");
                    table.Cell().Element(CellStyle).AlignCenter().Text(item.WillCallTime?.ToString(@"hh\:mm") ?? "");
                    table.Cell().Element(CellStyle).AlignRight().Text(item.Distance?.ToString("N2") ?? "0.00");
                    table.Cell().Element(CellStyle).AlignRight().Text(item.Charge?.ToString("C2") ?? "$0.00"); // Nota: Usé "C2" para que salga con formato de moneda ($) 

                    // --- CELDA DE LA FIRMA ---
                    // Usamos Constrained con Height fijo para evitar el error de conflicto de tamaños
                    var signatureCell = table.Cell().Element(CellStyle).Height(30);

                    if (item.PickupSignature != null && item.PickupSignature.Length > 0)
                    {
                        // Usamos FitArea para que la imagen se adapte sin forzar el tamaño del contenedor
                        signatureCell.AlignCenter().AlignMiddle().Image(item.PickupSignature, ImageScaling.FitArea);
                    }
                    else
                    {
                        signatureCell.Text("");
                    }
                }

                // Definición de estilo de celda reutilizable
                static IContainer CellStyle(IContainer container)
                {
                    return container.Border(0.5f).Padding(2).AlignMiddle();
                }
            });
        }

        private void ComposeFooter(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().PaddingTop(5).Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7));
                    t.Span("**NOTE***Leg of transport").Bold();
                    t.Span("-a leg of transport is the point of pick-up to the destination. Example: Picking recipient up at residence and transporting to the doctor's office would be considered one leg; picking recipient up at residence and transporting to the doctor's office would be considered one leg; picking the recipient up at the doctor's office and transporting back to the residence would be considered the second leg of the trip. Each leg of the transport must be documented on separate lines. A signature is required for each leg to the transport. Pick-up and drop-off time must be documented and in military time.");
                });

                col.Item().PaddingTop(5).Text("Driver's Comments:").Bold().FontSize(8);

                // Líneas de comentarios
                col.Item().PaddingTop(10).BorderBottom(0.5f);
                col.Item().PaddingTop(10).BorderBottom(0.5f);

                col.Item().PaddingTop(10).Text("I understand that ModivCare will verify the accuracy of the mileage being reported and hearby certify the information herein is true, and accurate.").Bold().FontSize(8);

                col.Item().PaddingTop(15).Row(row =>
                {
                    row.RelativeItem();
                    row.ConstantItem(250).Column(c =>
                    {
                        c.Item().AlignCenter().Text("DRIVER'S SIGNATURE:").Bold().FontSize(9);
                        //c.Item().PaddingVertical(2).LineHorizontal(1f);
                    });
                    row.RelativeItem();
                });
            });
        }
    }
}