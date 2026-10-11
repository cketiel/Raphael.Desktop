namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// GPS speed arrives in metres per second and the office reads miles per hour.
    /// </summary>
    /// <remarks>
    /// The Driver app sends MAUI's <c>Location.Speed</c> as is, on Android and on iOS, and the
    /// backend stores and relays it unchanged: every speed this app receives is m/s. Showing it with
    /// "mph" next to it understated it by more than half, so a vehicle at 70 mph read 31 and never
    /// turned the speeding colour. Convert here, at display, and nowhere upstream.
    /// </remarks>
    public static class SpeedUnits
    {
        /// <summary>1 m/s = 3600 / 1609.344 mph.</summary>
        public const double MphPerMetrePerSecond = 3600.0 / 1609.344;

        /// <summary>
        /// Converts a GPS speed to mph. A negative value is the platform saying "unknown"
        /// (iOS reports -1), which the office reads as stopped.
        /// </summary>
        public static double ToMph(double metresPerSecond) =>
            metresPerSecond > 0 ? metresPerSecond * MphPerMetrePerSecond : 0;
    }
}
