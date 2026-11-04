using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// The compliance documents (RE-065) as the rest of the Desktop shows them: the one-line status of
    /// a person or a vehicle in Users and Vehicles, and the way into Docs on that person or vehicle.
    /// Docs is the only place they are managed; the other screens only point to it.
    /// </summary>
    public static class ComplianceUi
    {
        private static string T(string key) => LocalizationService.Instance[key];

        /// <summary>danger, warn, info, ok or none: the colour of the badge.</summary>
        public static string StateOf(ComplianceSummaryDto s) =>
            s is null ? "none"
            : s.Expired + s.Rejected > 0 ? "danger"
            : s.Expiring > 0 ? "warn"
            : s.Pending > 0 ? "info"
            : s.Valid > 0 ? "ok" : "none";

        /// <summary>"2 expired", "1 expiring", "1 in review", "5/8 valid" or "None uploaded".</summary>
        public static string TextOf(ComplianceSummaryDto s)
        {
            if (s is null) return string.Empty;
            if (s.Expired > 0) return string.Format(T("compliance.BadgeExpired"), s.Expired);
            if (s.Rejected > 0) return string.Format(T("compliance.BadgeRejected"), s.Rejected);
            if (s.Expiring > 0) return string.Format(T("compliance.BadgeExpiring"), s.Expiring);
            if (s.Pending > 0) return string.Format(T("compliance.BadgePending"), s.Pending);
            if (s.Valid > 0) return string.Format(T("compliance.BadgeValid"), s.Valid, s.Total);
            return T("compliance.BadgeNone");
        }

        public static string FilesText(ComplianceDocumentDto d) =>
            d is null || d.Files.Count == 0 ? "—"
            : d.Files.Count > 1 ? string.Format(T("compliance.Photos"), d.Files.Count)
            : d.Files[0].ContentType == "application/pdf" ? "PDF" : T("compliance.Image");

        /// <summary>One request for the whole list; null on failure, and the list simply shows no badge.</summary>
        public static async Task<Dictionary<int, ComplianceSummaryDto>> SummariesAsync(string subject)
        {
            try
            {
                return (await new ComplianceService().GetOverviewAsync(subject)).ToDictionary(s => s.SubjectId);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Opens Docs on this person (<c>USER</c>) or vehicle (<c>VEHICLE</c>), and on one of its documents.</summary>
        public static void Open(string subject, int id, int? documentId = null)
        {
            if (Application.Current?.MainWindow is MainWindow main) main.OpenCompliance(subject, id, documentId);
        }
    }
}
