using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Lógica de interacción para AdminView.xaml
    /// </summary>
    public partial class AdminView : UserControl
    {
        public AdminView()
        {
            InitializeComponent();
            DataContext = new AdminViewModel();

            // RE-049: the platform's tabs are removed, not hidden, for a client company's admin.
            // The TabControl style builds every tab, and these load data the server refuses
            // (403) to anyone but Raphael's own admin.
            if (!Helpers.SessionManager.IsPlatformAdmin)
            {
                foreach (var tab in new[] { ProvidersTab, GoogleMapsTab })
                    (tab.Parent as System.Windows.Controls.ItemsControl)?.Items.Remove(tab);
            }
        }
    }
}
