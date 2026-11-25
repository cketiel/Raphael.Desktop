using Raphael.Desktop.ViewModels;
using System.Windows;

namespace Raphael.Desktop.Views.Reports
{
    public partial class OdometerCorrectionWindow : Window
    {
        private OdometerCorrectionWindow(OdometerTarget target, Window? owner)
        {
            InitializeComponent();
            Owner = owner ?? Application.Current?.MainWindow;
            var vm = new OdometerCorrectionViewModel(target);
            vm.Saved += () => DialogResult = true;
            DataContext = vm;
            Helpers.HelpAssist.SetTopicId(this, "desktop/data/odometer");
            Loaded += (_, _) => ReadingBox.Focus();
        }

        /// <summary>True when the correction was saved.</summary>
        public static bool Show(OdometerTarget target, Window? owner = null) => new OdometerCorrectionWindow(target, owner).ShowDialog() == true;
    }
}
