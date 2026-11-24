using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System.Linq;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// RE-083: the Desktop's copy of Raphael.Shared.Capability.VehicleCapabilityRule, and the words
    /// for it. A higher level carries the lower ones: STR carries WCH and AMB, WCH carries AMB.
    /// </summary>
    public static class VehicleCapabilityUi
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public static string? Kind(int level) => level switch { >= 3 => "STR", 2 => "WCH", 1 => "AMB", _ => null };

        public static bool CanServe(int vehicleLevel, int tripLevel) => vehicleLevel > 0 && tripLevel > 0 && vehicleLevel >= tripLevel;

        public static string KindText(int level) => Kind(level) ?? T("vehcap.NotConfigured");

        public static string SeatsText(VehicleConfigurationDto c)
        {
            var seats = c.Seats.Where(s => s.Quantity > 0).ToList();
            return seats.Count == 0
                ? T("vehcap.NoSeats")
                : string.Join("  ·  ", seats.Select(s => $"{s.SpaceType} {s.Quantity}" + (s.TravelsAlone ? $" ({T("vehcap.Alone")})" : "")));
        }

        /// <summary>
        /// Why a line's vehicle cannot carry a trip; null when it can. Only ever a warning: nothing
        /// is blocked (RE-083, phase 1).
        /// </summary>
        public static string? Explain(string? route, int vehicleLevel, string? spaceType, int tripLevel)
        {
            if (CanServe(vehicleLevel, tripLevel)) return null;
            if (tripLevel <= 0) return string.Format(T("vehcap.WarnNoClass"), spaceType);
            if (vehicleLevel <= 0) return string.Format(T("vehcap.WarnNoConfig"), route);
            return string.Format(T("vehcap.WarnCannot"), route, Kind(vehicleLevel), spaceType, Kind(tripLevel));
        }
    }
}
