#nullable enable
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System.Linq;
using System.Text;

namespace Raphael.Desktop.Helpers
{
    /// <summary>RE-099: the words of an Overflow trip. It marks and explains; routing it stays free.</summary>
    public static class OverflowText
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public static string Explain(PlannedTripDto t)
        {
            var b = new StringBuilder();
            b.AppendLine(T("overflow.Title"));
            foreach (var r in t.Reasons)
                b.AppendLine("• " + (r switch
                {
                    "NO_LINE" => T("overflow.NoLine"),
                    "LATE" => string.Format(T("overflow.Late"), t.LatePatients),
                    "LONG" => string.Format(T("overflow.Long"), t.Miles.ToString("0")),
                    _ => string.Format(T("overflow.Cost"), t.AddedMiles.ToString("0"), t.AddedMinutes, t.EqualsTrips.ToString("0.#")),
                }));
            b.AppendLine(string.Format(T("overflow.Cancel"), t.CancelPercent));
            if (t.DecideBy is { } by) b.AppendLine(string.Format(T(t.IsUrgent ? "overflow.Urgent" : "overflow.DecideBy"), FeasibilityText.Hour(by)));
            if (t.Line is { } line) b.AppendLine(string.Format(T("overflow.Line"), line));
            return b.ToString().TrimEnd();
        }
    }
}
