using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Raphael.Desktop.Converters
{
    public class IdToTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var baseTitle = parameter as string ?? "Editor";
            if (value is int id && id > 0)
            {
                return $"Editar {baseTitle} (ID: {id})";
            }
            return $"Nuevo {baseTitle}";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
