using System.Windows.Controls;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Compliance
{
    public partial class ComplianceView : UserControl
    {
        /// <summary>From the menu: To review in front. From Users or Vehicles: that person or vehicle, selected.</summary>
        public ComplianceView(string focusSubject = null, int focusId = 0, int? focusDocumentId = null)
        {
            InitializeComponent();
            DataContext = new ComplianceViewModel(focusSubject, focusId, focusDocumentId);
        }

        /// <summary>
        /// RE-069: the selected document is always on screen, so the one a notice opened is seen
        /// without scrolling for it. View only: no logic here.
        /// </summary>
        private void DocsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem != null)
                grid.Dispatcher.BeginInvoke(new System.Action(() => grid.ScrollIntoView(grid.SelectedItem)),
                    System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
