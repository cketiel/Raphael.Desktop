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
    }
}
