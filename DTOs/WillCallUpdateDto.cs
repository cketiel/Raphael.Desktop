namespace Raphael.Desktop.DTOs
{
    /// <summary>
    /// The pickup time a dispatcher settled on when moving a trip's Will Call state.
    /// </summary>
    /// <remarks>
    /// Copy of <c>Raphael.Shared.DTOs.WillCallUpdateDto</c>, kept in step by hand.
    /// See <c>_meta/CONTRACT_MAP.md</c>.
    ///
    /// <para>
    /// Only the time travels: the flag itself, the trip status and the history row are the
    /// server's business. Null means "use the operation's own clock".
    /// </para>
    /// </remarks>
    public class WillCallUpdateDto
    {
        public TimeSpan? FromTime { get; set; }
    }
}
