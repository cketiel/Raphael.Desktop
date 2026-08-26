namespace Raphael.Desktop.ViewModels;

/// <summary>
/// One labelled value inside a detail card.
/// </summary>
/// <remarks>
/// The trip, the patient, the driver and the line are forty-odd fields between them, and
/// which of them have a value changes with every trip. Building them here instead of in
/// the view means an empty field is simply not created: no card ever shows a label with a
/// dash after it, and the layout closes up around what is actually known.
/// </remarks>
public sealed class DetailFieldViewModel
{
    /// <summary>
    /// Width of an ordinary tile. Sized so that two of them, with their margins, still fit
    /// side by side inside the narrowest card in the panel — the 380-wide right column.
    /// A tile wider than its card does not wrap: it hangs out over the border.
    /// </summary>
    private const double NarrowWidth = 145;

    /// <summary>
    /// Width of a tile that holds prose — an address, a comment left for the driver.
    /// One per row, and still inside that same card.
    /// </summary>
    private const double WideWidth = 310;

    public DetailFieldViewModel(string label, string value, bool isWide = false)
    {
        Label = label;
        Value = value;
        IsWide = isWide;
    }

    public string Label { get; }

    public string Value { get; }

    public bool IsWide { get; }

    public double TileWidth => IsWide ? WideWidth : NarrowWidth;
}
