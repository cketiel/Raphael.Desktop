using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.Helpers
{
    /// <summary>RE-091: who cancelled a trip and why, in words. The codes are the server's.</summary>
    public static class Cancellations
    {
        public const string Other = "OTHER";

        /// <summary>What the office picks from. A no-show is the driver's to report.</summary>
        public static readonly IReadOnlyList<string> OfficeReasons = new[]
        {
            "PATIENT_REQUEST", "APPOINTMENT_CANCELLED", "APPOINTMENT_MOVED", "PATIENT_ILL", "OTHER_TRANSPORT",
            "PAYER_REQUEST", "NO_VEHICLE", "WEATHER", "DUPLICATE", Other
        };

        public static bool IsNoShow(TripReadDto trip) => !string.IsNullOrWhiteSpace(trip.DriverNoShowReason);

        /// <summary>Null when it is not cancelled, or was before anybody was written down.</summary>
        public static string? ActorOf(TripReadDto trip) =>
            !TripFigures.IsCanceled(trip) ? null : trip.CancelledBy ?? (IsNoShow(trip) ? "DRIVER" : null);

        public static string ActorLabel(string actor) =>
            LocalizationService.Instance.TryGetValue("cancel.by." + actor, out var label) ? label : actor;

        public static string ByText(TripReadDto trip)
        {
            if (ActorOf(trip) is not { } actor) return string.Empty;
            var name = trip.CancelledByName?.Trim();
            // The portal signs its users with its own name in front.
            foreach (var prefix in new[] { "BookingWeb - ", "Driver - ", "Rider - " })
                if (name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true) name = name[prefix.Length..];
            return string.IsNullOrEmpty(name) || actor == "BOT" ? ActorLabel(actor) : $"{ActorLabel(actor)} · {name}";
        }

        public static string ReasonLabel(string code) =>
            LocalizationService.Instance.TryGetValue("cancel.reason." + code, out var label) ? label : code;

        public static string ReasonText(TripReadDto trip)
        {
            if (!TripFigures.IsCanceled(trip)) return string.Empty;
            var text = trip.CancellationReason ?? trip.DriverNoShowReason;
            var code = trip.CancellationReasonCode ?? (IsNoShow(trip) ? "NO_SHOW" : null);
            if (code is null) return text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return ReasonLabel(code);
            return code == Other ? text : $"{ReasonLabel(code)}: {text}";
        }

        public static string? ToolTip(TripReadDto trip)
        {
            if (!TripFigures.IsCanceled(trip)) return null;
            var by = ByText(trip);
            var why = ReasonText(trip);
            var lines = new List<string>
            {
                by.Length > 0 ? string.Format(LocalizationService.Instance["cancel.tip.By"], by) : LocalizationService.Instance["cancel.tip.Unknown"]
            };
            if (trip.CancelledAtUtc is DateTime at)
                lines.Add(DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("MM/dd/yyyy h:mm tt"));
            if (why.Length > 0) lines.Add(why);
            return string.Join(Environment.NewLine, lines);
        }

        public static string Icon(string? actor) => actor switch
        {
            "DISPATCHER" => "Headset",
            "DRIVER" => "Steering",
            "RIDER" => "AccountOutline",
            "FACILITY" => "HospitalBuilding",
            "INTEGRATOR" => "Api",
            "PROVIDER" => "DomainSwitch",
            "BOT" => "RobotOutline",
            _ => "None"
        };
    }
}
