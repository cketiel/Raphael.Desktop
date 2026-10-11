using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Converters
{
    // We inherit from Freezable so we can instantiate it and configure it in the XAML resources.
    public class SpeedToBrushConverter : Freezable, IValueConverter
    {
        // Properties to configure speed thresholds
        public double StoppedThreshold { get; set; } = 2.0; // mph below which it is considered stopped
        public double SpeedingThreshold { get; set; } = 65.0; // mph above which is considered speeding

        // Properties to configure colors (Brushes)
        public Brush StoppedBrush { get; set; } = Brushes.SlateGray;
        public Brush NormalSpeedBrush { get; set; } = Brushes.DodgerBlue;
        public Brush SpeedingBrush { get; set; } = Brushes.Red;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double metresPerSecond)
            {
                // The thresholds are mph; the value is the GPS speed in m/s.
                double speed = SpeedUnits.ToMph(metresPerSecond);

                if (speed >= SpeedingThreshold)
                {
                    return SpeedingBrush; // Speeding
                }
                if (speed > StoppedThreshold)
                {
                    return NormalSpeedBrush; // normal speed
                }
                return StoppedBrush; // Stopped or moving very slowly
            }
            // Returns the brush stopped if the value is invalid
            return StoppedBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
     
        protected override Freezable CreateInstanceCore()
        {
            return new SpeedToBrushConverter();
        }
    }
}
