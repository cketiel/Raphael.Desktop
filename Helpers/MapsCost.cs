#nullable enable
using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// RE-099: the Desktop's copy of the server's MapsPricingCalculator, for the simulator: the price
    /// of some requests in a month that already has others, by Google's volume bands.
    /// </summary>
    public static class MapsCost
    {
        public static decimal Of(IReadOnlyList<MapsPricingTierDto> tiers, string sku, long requests, long alreadyUsed)
        {
            var bands = tiers.Where(t => t.Sku == sku).OrderBy(t => t.FromRequest).ToList();
            if (requests <= 0 || bands.Count == 0) return 0m;
            var first = Math.Max(alreadyUsed + 1, (long)bands[0].FreeCapPerMonth + 1);
            var last = alreadyUsed + requests;
            var total = 0m;
            foreach (var b in bands)
            {
                var from = Math.Max(first, (long)b.FromRequest);
                var to = Math.Min(last, b.ToRequest ?? long.MaxValue);
                if (from <= to) total += (to - from + 1) / 1000m * b.PricePerThousand;
            }
            return Math.Round(total, 2);
        }
    }

    /// <summary>What a choice of limits does in a month.</summary>
    public sealed record MapsLimitPlan(
        int RoadsPerTrip, int NeededPerDay, int BoughtPerDay, int TripsCovered, int TripsEstimated,
        decimal MaxMonth, decimal ExpectedMonth, decimal NetMonth, decimal TotalMonth)
    {
        /// <summary>The roads routing a trip buys anyway: to the pickup, to the dropoff and on to the next stop.</summary>
        public const int BoughtByRouting = 3;

        /// <summary>
        /// One road of the patient, the same for every line, and two per line: to the pickup and from the dropoff on.
        /// </summary>
        public static int RoadsFor(int top) => top <= 0 ? 0 : 1 + 2 * top;

        public static MapsLimitPlan Of(int top, int limit, int tripsPerDay, int days, int knownPercent,
            long baseRoads, IReadOnlyList<MapsPricingTierDto> tiers, string sku)
        {
            var roads = RoadsFor(top);
            // What the cache answers costs nothing and does not touch the limit.
            var perTrip = roads * (100 - Math.Clamp(knownPercent, 0, 100)) / 100.0;
            var needed = (int)Math.Ceiling(tripsPerDay * perTrip);
            var bought = Math.Min(needed, Math.Max(0, limit));
            var covered = roads == 0 ? 0 : perTrip <= 0 ? tripsPerDay : Math.Min(tripsPerDay, (int)Math.Floor(bought / perTrip + 1e-9));
            var month = (long)bought * days;
            var anyway = roads == 0 ? 0 : (long)Math.Round(month * (double)Math.Min(BoughtByRouting, roads) / roads);
            var expected = MapsCost.Of(tiers, sku, month, baseRoads);
            return new(roads, needed, bought, covered, Math.Max(0, tripsPerDay - covered),
                MapsCost.Of(tiers, sku, (long)Math.Max(0, roads == 0 ? 0 : limit) * days, baseRoads), expected,
                MapsCost.Of(tiers, sku, month - anyway, baseRoads),
                MapsCost.Of(tiers, sku, baseRoads + month - anyway, 0));
        }
    }
}
