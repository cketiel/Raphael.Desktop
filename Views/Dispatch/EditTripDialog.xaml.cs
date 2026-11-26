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

namespace Raphael.Desktop.Views.Dispatch
{
    /// <summary>
    /// Lógica de interacción para EditTripDialog.xaml
    /// </summary>
    public partial class EditTripDialog : UserControl
    {
        public EditTripDialog()
        {
            InitializeComponent();
        }

        // RE-094: an impossible or loose hour is explained once more before saving; Cancel keeps the dialog open.
        private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.EditTripDialogViewModel vm && !vm.ConfirmHours()) return;
            MaterialDesignThemes.Wpf.DialogHost.CloseDialogCommand.Execute(true, this);
        }
    }
}
