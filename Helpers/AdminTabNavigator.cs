using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Brings another tab of the Admin panel to the front.
    /// </summary>
    /// <remarks>
    /// There is no navigation service in this application — the Admin panel is a TabControl and
    /// nothing else. So "take me to the catalog" is: walk up to the TabControl that owns the
    /// tab I am in, and select the one whose header matches.
    ///
    /// <para>
    /// Matching on the header is weaker than matching on a type would be, and it is what the
    /// structure allows: the tab's content is a UserControl that only exists once the tab has
    /// been selected, because a TabControl realises one tab at a time. Looking for the view
    /// would find nothing until somebody had already been there.
    /// </para>
    /// </remarks>
    public static class AdminTabNavigator
    {
        /// <summary>
        /// Selects the sibling tab whose header reads <paramref name="header"/>.
        /// Returns false when there is no such tab, which is a caller's problem to report
        /// or ignore — never an exception in the middle of a click.
        /// </summary>
        public static bool SelectSiblingTab(DependencyObject from, string header)
        {
            var tabControl = FindAncestor<TabControl>(from);

            if (tabControl is null)
            {
                return false;
            }

            foreach (var item in tabControl.Items)
            {
                if (item is TabItem tab
                    && tab.Header is string text
                    && string.Equals(text, header, System.StringComparison.OrdinalIgnoreCase))
                {
                    tab.IsSelected = true;
                    return true;
                }
            }

            return false;
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current is not null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
