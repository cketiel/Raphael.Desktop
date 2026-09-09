using ClosedXML.Excel;
using Microsoft.Win32;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Writes the trips a dispatcher is looking at to a spreadsheet.
    /// </summary>
    /// <remarks>
    /// ⚠️ Everything this writes is PHI — patient names, addresses and phone numbers leaving the
    /// system in a file that nothing tracks afterwards. That is why it says so before writing and
    /// records who asked. See CLAUDE.md §3.
    /// </remarks>
    public class TripExcelExportService
    {
        /// <summary>
        /// Exports exactly the rows given, in the order given.
        /// </summary>
        /// <remarks>
        /// It takes rows rather than a date range on purpose: what leaves has to be what the
        /// dispatcher can see, filters and all. A spreadsheet that quietly holds more trips than
        /// the screen did is a spreadsheet nobody can check.
        /// </remarks>
        public void Export(IReadOnlyList<TripReadDto> trips, DateTime day)
        {
            if (trips == null || trips.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.Instance["home.ExportNoRows"],
                    LocalizationService.Instance["home.ExportTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            var warning = MessageBox.Show(
                string.Format(LocalizationService.Instance["home.ExportPhiMessage"], trips.Count),
                LocalizationService.Instance["home.ExportPhiTitle"],
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (warning != MessageBoxResult.OK) return;

            var dialog = new SaveFileDialog
            {
                Filter = "Excel Workbook|*.xlsx",
                Title = LocalizationService.Instance["home.ExportTitle"],
                FileName = $"Trips_{day:yyyy-MM-dd}.xlsx"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                Write(trips, dialog.FileName);

                // Who took the file, how many rows, and for which day. Nothing else follows this
                // spreadsheet once it is on disk, so this line is the only record there will be.
                FileLogger.Log(
                    $"PHI export: {SessionManager.Username} wrote {trips.Count} trips of " +
                    $"{day:yyyy-MM-dd} to {dialog.FileName}");

                var open = MessageBox.Show(
                    string.Format(LocalizationService.Instance["home.ExportDone"], dialog.FileName),
                    LocalizationService.Instance["home.ExportTitle"],
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (open == MessageBoxResult.Yes)
                    Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    LocalizationService.Instance["home.ExportFailed"] + ex.Message,
                    LocalizationService.Instance["home.ExportTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                FileLogger.Log(ex);
            }
        }

        private static void Write(IReadOnlyList<TripReadDto> trips, string path)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Trips");

            var headers = new[]
            {
                "Trip Id", "Id", "Date", "Day", "From", "To", "Patient",
                "Pickup Address", "Pickup City", "Dropoff Address", "Dropoff City",
                "Space Type", "Funding Source", "Type", "Will Call", "Status", "Run",
                "Distance", "Charge", "Paid", "Authorization",
                "Pickup Phone", "Dropoff Phone", "Pickup Comment", "Dropoff Comment"
            };

            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];

            var headerRow = sheet.Range(1, 1, 1, headers.Length);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.LightGray;
            headerRow.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var row = 2;
            foreach (var trip in trips)
            {
                var col = 1;

                sheet.Cell(row, col++).Value = trip.TripId;
                sheet.Cell(row, col++).Value = trip.Id;

                // A date written as a date, so a spreadsheet can sort and filter it. Written as
                // text it sorts alphabetically, which puts the 10th before the 2nd.
                var date = sheet.Cell(row, col++);
                date.Value = trip.Date;
                date.Style.DateFormat.Format = "yyyy-MM-dd";

                sheet.Cell(row, col++).Value = trip.Day;
                sheet.Cell(row, col++).Value = Time(trip.FromTime);
                sheet.Cell(row, col++).Value = Time(trip.ToTime);
                sheet.Cell(row, col++).Value = trip.CustomerName;
                sheet.Cell(row, col++).Value = trip.PickupAddress;
                sheet.Cell(row, col++).Value = trip.PickupCity;
                sheet.Cell(row, col++).Value = trip.DropoffAddress;
                sheet.Cell(row, col++).Value = trip.DropoffCity;
                sheet.Cell(row, col++).Value = trip.SpaceTypeName;
                sheet.Cell(row, col++).Value = trip.FundingSourceName;
                sheet.Cell(row, col++).Value = trip.Type;
                sheet.Cell(row, col++).Value = trip.WillCall ? "Yes" : "No";
                sheet.Cell(row, col++).Value = trip.Status;
                sheet.Cell(row, col++).Value = trip.RunName;
                sheet.Cell(row, col++).Value = trip.Distance;
                sheet.Cell(row, col++).Value = trip.Charge;
                sheet.Cell(row, col++).Value = trip.Paid;
                sheet.Cell(row, col++).Value = trip.Authorization;
                sheet.Cell(row, col++).Value = trip.PickupPhone;
                sheet.Cell(row, col++).Value = trip.DropoffPhone;
                sheet.Cell(row, col++).Value = trip.PickupComment;
                sheet.Cell(row, col++).Value = trip.DropoffComment;

                row++;
            }

            sheet.Range(1, 1, Math.Max(row - 1, 1), headers.Length).SetAutoFilter();
            sheet.Columns().AdjustToContents();

            workbook.SaveAs(path);
        }

        private static string Time(TimeSpan? value) => value?.ToString(@"hh\:mm") ?? string.Empty;
    }
}
