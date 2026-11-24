using Microsoft.AspNetCore.SignalR.Client;
using System;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// RE-090: a live channel never gives up. The default policy does, after about forty seconds, and
    /// a screen that stopped listening looks exactly like a quiet morning.
    /// </summary>
    public sealed class LiveReconnectPolicy : IRetryPolicy
    {
        private static readonly int[] Seconds = { 0, 1, 2, 5, 10 };

        public static TimeSpan Delay(long attempt)
        {
            // A little apart from each other: a server that comes back is not asked by everybody at once.
            var seconds = attempt < Seconds.Length ? Seconds[attempt] : 15;
            return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(seconds == 0 ? 0 : Random.Shared.Next(0, 800));
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => Delay(retryContext.PreviousRetryCount);
    }
}
