using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Linq;
using System.Windows.Input;
using System.Windows.Controls; 

namespace Raphael.Desktop.Views.Data.Scheduling.Vehicles
{
    public partial class AddEditGroupWindow : Window
    {
        public AddEditGroupWindow(AddEditGroupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {            
            var focusedElement = FocusManager.GetFocusedElement(this);
            if (focusedElement is FrameworkElement fe)
            {
                var binding = fe.GetBindingExpression(TextBox.TextProperty); 
                binding?.UpdateSource();
            }

            var vm = DataContext as AddEditGroupViewModel;
            if (vm != null)
            {               
                if (string.IsNullOrWhiteSpace(vm.CurrentGroup.Name))
                {                   
                    MessageBox.Show(Services.LocalizationService.Instance["NameIsRequiredError"],
                                    Services.LocalizationService.Instance["ValidationErrorTitle"],
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}