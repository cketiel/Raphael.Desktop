using Raphael.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace Raphael.Desktop.Views
{
    /// <summary>RE-095: the question about the pickup hours of a file just imported.</summary>
    public partial class ImportHoursWindow : Window
    {
        private bool _closing;

        public ImportHoursWindow()
        {
            InitializeComponent();
            DataContextChanged += (_, e) => { if (e.NewValue is ImportHoursViewModel vm) vm.CloseRequested += () => { _closing = true; Close(); }; };
        }

        // The cross means "leave the rest as it came", as the button does.
        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (_closing || DataContext is not ImportHoursViewModel vm) return;
            e.Cancel = true;
            vm.CloseCommand.Execute(null);
        }
    }
}
