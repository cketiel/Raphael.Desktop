namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// The handful of running settings this screen needs in order to draw itself.
    /// </summary>
    /// <remarks>
    /// Manual copy of <c>Raphael.Shared/DTOs/DispatchSettingsDto.cs</c>, which is the source of
    /// truth. Read-only: changing a setting stays behind the administrator's panel.
    /// </remarks>
    public class DispatchSettingsDto
    {
        /// <summary>
        /// Minutes of driver waiting at a pickup from which the whole row is marked. The chip on
        /// the arrival hour shows any wait at all; this is only where it becomes worth a colour.
        /// </summary>
        public int EarlyArrivalWaitHighlightMinutes { get; set; }
    }
}
