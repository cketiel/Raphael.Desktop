using GMap.NET;
using GMap.NET.WindowsPresentation;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Convert geographic coordinates (Latitude/Longitude) to an on-screen position (X or Y)
    /// to be used in a Canvas within a GMapControl.
    /// Also applies a vertical scroll for overlapping markers.
    /// </summary>
    public class LocationToPointConverter : IMultiValueConverter
    {
        /// <summary>
        /// The number of pixels to vertically shift each overlapping marker.
        /// A value slightly larger than the marker height works well to avoid overlapping.
        /// </summary>
        private const double VerticalOffsetAmount = 22.0;

        /// <summary>
        /// The main conversion method.
        /// </summary>
        /// <param name="values">An array of objects passed from MultiBinding in XAML. It is expected to contain:
        /// [0]: The GMapControl (MapView) control
        /// [1]: Latitude (double)
        /// [2]: Longitude (double)
        /// [3]: The size of the element (ActualWidth or ActualHeight, double)
        /// [4]: The Zoom level of the map (int)
        /// [5]: The center position of the map (PointLatLng)
        /// [6]: The visual offset index (VisualOffsetIndex, int)
        /// </param>
        /// <param name="targetType">The data type expected by the binding (should be double).</param>
        /// <param name="parameter">An optional parameter. "Y" is used to calculate the Top(Y) coordinate, otherwise it calculates Left(X).</param>
        /// <param name="culture">Culture information (not used).</param>
        /// <returns>The coordinate in pixels (double) for Canvas.Left or Canvas.Top.</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            
            if (values.Length < 6 || values.Take(6).Any(v => v == null || v == DependencyProperty.UnsetValue))
            {
                return 0.0;
            }

            try
            {               
                var gmap = (GMapControl)values[0];
                var latitude = (double)values[1];
                var longitude = (double)values[2];
                var elementSize = (double)values[3];
                
                int visualOffsetIndex = 0; 
                if (values.Length > 6 && values[6] is int providedIndex)
                {
                    visualOffsetIndex = providedIndex;
                }
                //var visualOffsetIndex = (int)values[6];

                // We use the GMapControl function to convert the geo coordinates to pixels on the screen.
                GPoint gPoint = gmap.FromLatLngToLocal(new PointLatLng(latitude, longitude));

                // We explicitly convert the GPoint to the WPF point type (Point)
                Point screenPoint = new Point(gPoint.X, gPoint.Y);

                // Determine if we calculate X or Y and apply displacements
                if (parameter?.ToString() == "Y")
                {
                    // We are calculating Canvas.Top (the Y coordinate)

                    // a) We calculate the base Y position to center the marker vertically.
                    double baseY = screenPoint.Y - (elementSize / 2);

                    // b) We apply vertical scrolling if the index is greater than 0.
                    double finalOffsetY = baseY + (visualOffsetIndex * VerticalOffsetAmount);

                    return finalOffsetY;
                }
                else
                {
                    // We are calculating Canvas.Left (the X coordinate)

                    // a) We calculate the base X position to center the marker horizontally.
                    // No horizontal scrolling is applied in this case.
                    double baseX = screenPoint.X - (elementSize / 2);
                    return baseX;
                }
            }
            catch (Exception ex)
            {                
                System.Diagnostics.Debug.WriteLine($"LocationToPointConverter Error: {ex.Message}");
                return 0.0;
            }
        }

        /// <summary>
        /// Reverse conversion is not necessary for this functionality.
        /// </summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}