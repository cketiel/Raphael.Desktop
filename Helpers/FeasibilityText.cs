#nullable enable
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Raphael.Desktop.Helpers
{
    /// <summary>RE-094/095: the words of the appointment check. It explains and suggests; it changes nothing.</summary>
    public static class FeasibilityText
    {
        public const int Tight = 2, Impossible = 3, BadHours = 4, Loose = 5;
        public const int Adjusted = 1, Declined = 2, Reverted = 3, ByHand = 4;

        private static string T(string key) => LocalizationService.Instance[key];
        public static string Hour(TimeSpan? t) => t is { } v ? DateTime.Today.Add(v).ToString("h:mm tt", CultureInfo.InvariantCulture) : "—";
        public static bool IsImpossible(TripFeasibilityDto? f) => f?.Kind is Impossible or BadHours;

        public static string Title(TripFeasibilityDto f) => f.Kind switch
        {
            BadHours => T("feas.TitleBadHours"),
            Loose => string.Format(T("feas.TitleLoose"), f.SlackMinutes),
            Impossible => string.Format(T("feas.TitleImpossible"), f.MissingMinutes),
            _ => T("feas.TitleTight"),
        };

        /// <summary>The whole sum, a line each.</summary>
        public static string Explain(TripFeasibilityDto f)
        {
            var b = new StringBuilder();
            b.AppendLine(Title(f));
            if (f.Kind == BadHours)
            {
                b.AppendLine(string.Format(T("feas.BadHours"), Hour(f.OriginalFromTime ?? f.FromTime), Hour(f.OriginalToTime ?? f.ToTime)));
                if (f.HintToTime is { } hint) b.AppendLine(string.Format(T("feas.HintTo"), Hour(hint)));
                return b.ToString().TrimEnd();
            }
            b.AppendLine(string.Format(T("feas.Available"), Hour(f.OriginalFromTime ?? f.FromTime), Hour(f.OriginalToTime ?? f.ToTime), f.AvailableMinutes));
            b.AppendLine(string.Format(T(f.LeadMinutes > 0 ? "feas.NeededLead" : "feas.Needed"), f.BoardingMinutes, f.DriveMinutes, (f.DriveMinutes ?? 0) + f.BoardingMinutes + f.LeadMinutes, f.LeadMinutes));
            b.AppendLine(f.DriveIsPriced ? T("feas.RoadGoogle") : T("feas.RoadEstimated"));
            if (f.LatestPickup is { } latest) b.AppendLine(string.Format(T("feas.Latest"), Hour(latest)));
            if (f.SuggestedFromTime is { } hour) b.AppendLine(Suggestion(f, hour));
            return b.ToString().TrimEnd();
        }

        private static string Suggestion(TripFeasibilityDto f, TimeSpan hour)
        {
            var boarded = hour + TimeSpan.FromMinutes(f.BoardingMinutes);
            var arrives = boarded + TimeSpan.FromMinutes(f.SuggestedDriveMinutes ?? f.DriveMinutes ?? 0);
            return string.Format(T(f.Kind == Loose ? "feas.SuggestedLoose" : "feas.Suggested"), Hour(hour), Hour(boarded), Hour(arrives), (f.OriginalToTime ?? f.ToTime) is { } to ? (int)(to - arrives).TotalMinutes : 0);
        }

        /// <summary>What was done with the hour, in a line.</summary>
        public static string AdjustmentText(TripFeasibilityDto f) => f.Adjustment switch
        {
            Adjusted => string.Format(T("feas.AdjustedTo"), Hour(f.AdjustedFromTime), f.AdjustedBy),
            Declined => T("feas.Declined"),
            Reverted => string.Format(T("feas.Reverted"), Hour(f.AdjustedFromTime)),
            ByHand => string.Format(T("feas.ByHand"), Hour(f.FromTime)),
            _ => T("feas.NotAdjusted"),
        };
    }
}
