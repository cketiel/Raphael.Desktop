using System.Windows.Controls;

namespace Raphael.Desktop.Views.Admin.Catalog
{
    /// <summary>
    /// Holds the two catalogs side by side. No ViewModel of its own: each tab builds its own,
    /// and only the selected tab's content is realised.
    /// </summary>
    public partial class CatalogView : UserControl
    {
        public CatalogView()
        {
            InitializeComponent();
        }
    }
}
