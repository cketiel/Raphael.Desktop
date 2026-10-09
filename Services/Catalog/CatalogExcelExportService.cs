using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using ClosedXML.Excel;
using Microsoft.Win32;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// Writes the catalog rows somebody is looking at to a spreadsheet.
    /// </summary>
    /// <remarks>
    /// The counterpart of the import, and the reason both exist: this is the list people work
    /// the phone from, and a list you cannot take with you is a list you cannot work from.
    ///
    /// <para>
    /// Unlike the trip export this carries no PHI — a catalog row is a company, its switchboard
    /// and the name of an administrator. It still records who took it: a file of several
    /// thousand contacts leaving the building is worth a line in the log.
    /// </para>
    /// </remarks>
    public static class CatalogExcelExportService
    {
        public static void ExportIntegrators(IReadOnlyList<CatalogIntegratorListItemDto> rows)
        {
            Export(
                rows.Count,
                "Catalog_Integrators",
                sheet =>
                {
                    var headers = new[]
                    {
                        LocalizationService.Instance["catalog.ColumnName"],
                        LocalizationService.Instance["catalog.ColumnGroup"],
                        LocalizationService.Instance["catalog.ColumnFacilityType"],
                        LocalizationService.Instance["catalog.ColumnBeds"],
                        LocalizationService.Instance["catalog.LabelAddress"],
                        LocalizationService.Instance["catalog.ColumnCity"],
                        LocalizationService.Instance["catalog.ColumnCounty"],
                        LocalizationService.Instance["catalog.LabelState"],
                        LocalizationService.Instance["catalog.ColumnZip"],
                        LocalizationService.Instance["catalog.ColumnPhone"],
                        LocalizationService.Instance["catalog.ColumnEmail"],
                        LocalizationService.Instance["catalog.LabelWebsite"],
                        LocalizationService.Instance["catalog.ColumnContact"],
                        LocalizationService.Instance["catalog.ColumnInMyCompany"]
                    };

                    WriteHeader(sheet, headers);

                    var line = 2;

                    foreach (var row in rows)
                    {
                        sheet.Cell(line, 1).Value = row.Name;
                        sheet.Cell(line, 2).Value = row.CategoryCode;
                        sheet.Cell(line, 3).Value = row.FacilityType;
                        if (row.Beds.HasValue) sheet.Cell(line, 4).Value = row.Beds.Value;
                        sheet.Cell(line, 6).Value = row.City;
                        sheet.Cell(line, 7).Value = row.CountyName;
                        sheet.Cell(line, 8).Value = row.State;

                        // As text: a ZIP is an identifier, and 32114 read as a number loses the
                        // leading zero that some of them have.
                        sheet.Cell(line, 9).SetValue(row.Zip);
                        sheet.Cell(line, 9).Style.NumberFormat.Format = "@";

                        sheet.Cell(line, 10).SetValue(row.Phone);
                        sheet.Cell(line, 10).Style.NumberFormat.Format = "@";

                        sheet.Cell(line, 11).Value = row.Email;
                        sheet.Cell(line, 12).Value = row.Website;
                        sheet.Cell(line, 13).Value = row.ContactName;
                        sheet.Cell(line, 14).Value = row.InMyCompany ? "X" : string.Empty;

                        line++;
                    }

                    return headers.Length;
                });
        }

        public static void ExportProviders(IReadOnlyList<CatalogProviderListItemDto> rows)
        {
            Export(
                rows.Count,
                "Catalog_Providers",
                sheet =>
                {
                    var headers = new[]
                    {
                        LocalizationService.Instance["catalog.ColumnName"],
                        LocalizationService.Instance["catalog.ColumnGroup"],
                        LocalizationService.Instance["catalog.ColumnNpi"],
                        LocalizationService.Instance["catalog.ColumnServiceLevel"],
                        LocalizationService.Instance["catalog.LabelAddress"],
                        LocalizationService.Instance["catalog.ColumnCity"],
                        LocalizationService.Instance["catalog.ColumnCounty"],
                        LocalizationService.Instance["catalog.LabelState"],
                        LocalizationService.Instance["catalog.ColumnZip"],
                        LocalizationService.Instance["catalog.ColumnPhone"],
                        LocalizationService.Instance["catalog.ColumnEmail"],
                        LocalizationService.Instance["catalog.LabelWebsite"],
                        LocalizationService.Instance["catalog.ColumnContact"],
                        LocalizationService.Instance["catalog.LabelLicenseExpires"],
                        LocalizationService.Instance["catalog.ColumnInMyCompany"]
                    };

                    WriteHeader(sheet, headers);

                    var line = 2;

                    foreach (var row in rows)
                    {
                        sheet.Cell(line, 1).Value = row.Name;
                        sheet.Cell(line, 2).Value = row.CategoryCode;

                        sheet.Cell(line, 3).SetValue(row.Npi);
                        sheet.Cell(line, 3).Style.NumberFormat.Format = "@";

                        sheet.Cell(line, 4).Value = row.ServiceLevel;
                        sheet.Cell(line, 6).Value = row.City;
                        sheet.Cell(line, 7).Value = row.CountyName;
                        sheet.Cell(line, 8).Value = row.State;

                        sheet.Cell(line, 9).SetValue(row.Zip);
                        sheet.Cell(line, 9).Style.NumberFormat.Format = "@";

                        sheet.Cell(line, 10).SetValue(row.Phone);
                        sheet.Cell(line, 10).Style.NumberFormat.Format = "@";

                        sheet.Cell(line, 11).Value = row.Email;
                        sheet.Cell(line, 12).Value = row.Website;
                        sheet.Cell(line, 13).Value = row.ContactName;

                        if (row.LicenseExpiresOn.HasValue)
                        {
                            sheet.Cell(line, 14).Value = row.LicenseExpiresOn.Value;
                            sheet.Cell(line, 14).Style.NumberFormat.Format = "yyyy-mm-dd";
                        }

                        sheet.Cell(line, 15).Value = row.InMyCompany ? "X" : string.Empty;

                        line++;
                    }

                    return headers.Length;
                });
        }

        private static void WriteHeader(IXLWorksheet sheet, IReadOnlyList<string> headers)
        {
            for (var i = 0; i < headers.Count; i++)
            {
                sheet.Cell(1, i + 1).Value = headers[i];
            }

            var header = sheet.Range(1, 1, 1, headers.Count);
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEEEE");
            sheet.SheetView.FreezeRows(1);
        }

        private static void Export(int count, string namePrefix, Func<IXLWorksheet, int> write)
        {
            if (count == 0)
            {
                MessageBox.Show(
                    LocalizationService.Instance["catalog.ExportNoRows"],
                    LocalizationService.Instance["catalog.Export"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                Title = LocalizationService.Instance["catalog.Export"],
                FileName = $"{namePrefix}_{DateTime.Now:yyyy-MM-dd}.xlsx"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                using var workbook = new XLWorkbook();
                var sheet = workbook.Worksheets.Add("Catalog");

                var columns = write(sheet);

                sheet.Columns(1, columns).AdjustToContents();
                workbook.SaveAs(dialog.FileName);

                FileLogger.Log(
                    $"Catalog export: {SessionManager.Username} wrote {count} rows to {dialog.FileName}");

                var open = MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.ExportDone"], count, dialog.FileName),
                    LocalizationService.Instance["catalog.Export"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (open == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.ExportFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
