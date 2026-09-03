namespace Raphael.Desktop.Helpers.Maps
{
    /// <summary>
    /// Something that knows where it belongs on the map.
    ///
    /// Implemented by the items bound to the marker layers of the Schedule screen, so the
    /// layer can ask each one for its coordinates directly instead of reaching into it
    /// through a binding path.
    /// </summary>
    public interface IMapMarker
    {
        double MarkerLatitude { get; }

        double MarkerLongitude { get; }

        /// <summary>
        /// How many markers sit on this exact spot before this one. Stops several stops at the
        /// same address from being drawn on top of each other; 0 means "nothing underneath".
        /// </summary>
        int MarkerOffsetIndex { get; }
    }
}
