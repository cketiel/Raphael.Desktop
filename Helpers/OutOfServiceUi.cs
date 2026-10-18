using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// What Runs, Vehicles and Users share about Out of Service (RE-053): the dialog, putting back
    /// in service, and the words for a vehicle's or driver's state.
    /// </summary>
    public static class OutOfServiceUi
    {
        private static LocalizationService L => LocalizationService.Instance;

        /// <summary>Opens the dialog. True when something was taken out of service.</summary>
        public static bool Show(OutOfServiceDialog dialog)
        {
            dialog.Owner = Application.Current?.MainWindow;
            dialog.ShowDialog();

            // By the result, not by how the window was closed: closing it with the X after it is
            // done still leaves trips moved, and the screen behind has to reload.
            return dialog.Result is not null;
        }

        /// <summary>"Out of service since 10/16 08:12 · Mechanical failure", or null when in service.</summary>
        public static string? Describe(IEnumerable<OutOfServiceRecordDto> records)
        {
            var list = records?.ToList() ?? new List<OutOfServiceRecordDto>();
            if (list.Count == 0) return null;

            return string.Join(" · ", list.Select(r => string.Format(L["oos.Status"],
                r.StartedAtUtc.ToLocalTime().ToString("g"), L["oos.reason." + r.ReasonCode])
                + (string.IsNullOrWhiteSpace(r.ReasonNote) ? string.Empty : $" ({r.ReasonNote})")));
        }

        /// <summary>
        /// Puts the records back in service after asking. Their trips do not come back, and the
        /// question says so. True when at least one went back.
        /// </summary>
        public static async Task<bool> ReturnToServiceAsync(IEnumerable<OutOfServiceRecordDto> records, string who)
        {
            var list = records?.ToList() ?? new List<OutOfServiceRecordDto>();
            if (list.Count == 0) return false;

            // What goes back is the vehicle or the driver, not the route the button sits on:
            // "Put ZZ-R1 back in service?" read as if the route itself had been out.
            var named = list.Where(r => !string.IsNullOrWhiteSpace(r.SubjectName))
                .Select(r => string.Format(L[r.Subject == OutOfServiceDialog.Vehicle ? "oos.SubjectVehicle" : "oos.SubjectDriver"], r.SubjectName))
                .ToList();
            if (named.Count > 0) who = string.Join(L["oos.And"], named);

            var answer = MessageBox.Show(Application.Current?.MainWindow,
                string.Format(L["oos.ReturnConfirm"], who),
                L["oos.ReturnTitle"], MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return false;

            var service = new OutOfServiceService();
            try
            {
                foreach (var record in list)
                    await service.ReturnToServiceAsync(record.Id);
                return true;
            }
            catch (Exception ex)
            {
                UiError.Show(ex.Message, L["oos.ReturnTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
                return true;
            }
        }
    }
}
