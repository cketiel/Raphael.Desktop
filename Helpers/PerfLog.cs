using System;
using System.Diagnostics;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Stopwatch traces for the screens whose slowness we are chasing.
    ///
    /// The point is to separate the three costs that a user experiences as one
    /// "the tab is slow": the wait on the network, the work done on the data once it
    /// arrives, and the time WPF spends building rows. Optimising the wrong one of the
    /// three is how a screen gets rewritten and stays slow.
    ///
    /// Every call compiles away outside DEBUG: <see cref="Debug.WriteLine(string)"/> is
    /// annotated <c>[Conditional("DEBUG")]</c>, and so is <see cref="Mark"/>. The scope
    /// returned by <see cref="Measure"/> still allocates in Release, so it is a struct
    /// and its stopwatch is only started when tracing is on.
    /// </summary>
    internal static class PerfLog
    {
#if DEBUG
        private const bool Enabled = true;
#else
        private const bool Enabled = false;
#endif

        /// <summary>
        /// Times the block it wraps. Use it with <c>using</c>:
        /// <code>using (PerfLog.Measure("Schedule.Load.Total")) { … }</code>
        /// </summary>
        public static Scope Measure(string label) => new Scope(label);

        /// <summary>
        /// Records a duration measured elsewhere — a task that was awaited in parallel with
        /// others, where a wrapping scope would report the wall clock of the whole batch.
        /// </summary>
        [Conditional("DEBUG")]
        public static void Mark(string label, long elapsedMs, int? count = null)
        {
            var suffix = count.HasValue ? $"  ({count} items)" : string.Empty;
            Debug.WriteLine($"[perf] {label,-44} {elapsedMs,6} ms{suffix}");
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly string _label;
            private readonly long _startedAt;

            public Scope(string label)
            {
                _label = label;
                _startedAt = Enabled ? Stopwatch.GetTimestamp() : 0L;
            }

            public void Dispose()
            {
                if (!Enabled) return;

                var elapsed = (Stopwatch.GetTimestamp() - _startedAt) * 1000L / Stopwatch.Frequency;
                Mark(_label, elapsed);
            }
        }
    }
}
