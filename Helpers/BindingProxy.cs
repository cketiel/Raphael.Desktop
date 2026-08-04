using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.Helpers
{
    public class BindingProxy : Freezable
    {
        // 1) we define the Data property
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(object),
                typeof(BindingProxy),
                new PropertyMetadata(null)
            );

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        // 2) Freezable needs this override
        protected override Freezable CreateInstanceCore()
            => new BindingProxy();
    }
}
