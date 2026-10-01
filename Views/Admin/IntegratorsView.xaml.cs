using Raphael.Desktop.ViewModels;
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

namespace Raphael.Desktop.Views.Admin
{
    /// <summary>
    /// Lógica de interacción para IntegratorsView.xaml
    /// </summary>
    public partial class IntegratorsView : UserControl
    {
        public IntegratorsView()
        {
            InitializeComponent();
            this.DataContext = new IntegratorsViewModel();
        }

        /// <summary>
        /// Takes the user to the catalog tab: the entities we could work with, as opposed to
        /// the ones on this screen, which are the ones we do.
        /// </summary>
        private void OpenCatalog_Click(object sender, RoutedEventArgs e)
        {
            Helpers.AdminTabNavigator.SelectSiblingTab(
                this, Services.LocalizationService.Instance["catalog.Tab"]);
        }

    }
}
