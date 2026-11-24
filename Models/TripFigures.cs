using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.Models
{
    /// <summary>
    /// RE-084: how many trips there are and where each one stands. Every trip falls in exactly one
    /// of the five parts, so Total = Done + InProgress + NotStarted + Canceled + NoRun.
    /// </summary>
    /// <remarks>
    /// The order of the questions is the rule. Canceled first: a canceled trip can keep its run, and
    /// can be marked canceled with another status. Then what already happened, which wins over the
    /// run. Only then, with nothing driven yet, whether there is a run: NoRun is what is left to route.
    /// </remarks>
    public sealed record TripFigures(int Total, int Done, int InProgress, int NotStarted, int Canceled, int NoRun)
    {
        public static TripFigures Empty { get; } = new(0, 0, 0, 0, 0, 0);

        public static TripFigures Count(IEnumerable<TripReadDto> trips)
        {
            int total = 0, done = 0, inProgress = 0, notStarted = 0, canceled = 0, noRun = 0;
            foreach (var t in trips)
            {
                total++;
                if (IsCanceled(t)) canceled++;
                else if (Is(t, TripStatus.Finished) || Is(t, TripStatus.Billed) || Is(t, TripStatus.Payed)) done++;
                else if (Is(t, TripStatus.Started) || Is(t, TripStatus.Arrived) || Is(t, TripStatus.InProgress)) inProgress++;
                else if (string.IsNullOrWhiteSpace(t.RunName)) noRun++;
                else notStarted++;
            }
            return new(total, done, inProgress, notStarted, canceled, noRun);
        }

        public static bool IsCanceled(TripReadDto trip) => trip.IsCancelled || Is(trip, TripStatus.Canceled);

        private static bool Is(TripReadDto trip, string status) => string.Equals(trip.Status, status, StringComparison.OrdinalIgnoreCase);
    }
}
