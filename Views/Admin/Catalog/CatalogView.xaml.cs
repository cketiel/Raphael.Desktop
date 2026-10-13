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

            // RE-049: a client company's admin works the Integrators catalog only (D3). The
            // Providers tab is removed before it loads: the server refuses it to them (403).
            if (!Helpers.SessionManager.IsPlatformAdmin)
                (CatalogProvidersTab.Parent as ItemsControl)?.Items.Remove(CatalogProvidersTab);
        }
    }
}
