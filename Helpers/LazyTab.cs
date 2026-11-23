using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// A tab whose view is built the first time it is opened, and kept from then on.
    /// </summary>
    /// <remarks>
    /// A view written straight inside a TabItem is constructed with the TabControl, selected or
    /// not, and its ViewModel starts loading at once. Admin had eight of them: opening it built
    /// Users, Roles, Billing, Profile, Integrators, Providers, Catalog and Google Maps together,
    /// every one reading the API and filling grids on the UI thread, so the one tab on screen
    /// waited behind seven nobody had asked for (RE-082: a 46 ms read took 500 ms to arrive).
    ///
    /// A DataTemplate as the TabItem's ContentTemplate would be lazy too, but a TabControl
    /// re-applies it on every switch, so each return to a tab would rebuild it and read the API
    /// again. Here the template is loaded once into the TabItem's Content, and the same view is
    /// shown every time after that.
    ///
    /// Usage: <c>&lt;TabItem&gt;&lt;helpers:LazyTab.View&gt;&lt;DataTemplate&gt;&lt;views:X/&gt;&lt;/DataTemplate&gt;&lt;/helpers:LazyTab.View&gt;&lt;/TabItem&gt;</c>
    /// </remarks>
    public static class LazyTab
    {
        public static readonly DependencyProperty ViewProperty = DependencyProperty.RegisterAttached(
            "View", typeof(DataTemplate), typeof(LazyTab), new PropertyMetadata(null, OnViewChanged));

        public static DataTemplate? GetView(DependencyObject o) => (DataTemplate?)o.GetValue(ViewProperty);
        public static void SetView(DependencyObject o, DataTemplate? value) => o.SetValue(ViewProperty, value);

        private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TabItem tab || DesignerProperties.GetIsInDesignMode(d)) return;
            tab.RemoveHandler(Selector.SelectedEvent, (RoutedEventHandler)OnSelected);
            tab.AddHandler(Selector.SelectedEvent, (RoutedEventHandler)OnSelected);
            if (tab.IsSelected) Realise(tab);
            else tab.Loaded += (_, _) => { if (tab.IsSelected) Realise(tab); };
        }

        private static void OnSelected(object sender, RoutedEventArgs e)
        {
            // Selected bubbles: a nested TabControl's tab would arrive here too.
            if (ReferenceEquals(e.OriginalSource, sender) && sender is TabItem tab) Realise(tab);
        }

        private static void Realise(TabItem tab)
        {
            if (tab.Content is not null || GetView(tab) is not DataTemplate template) return;
            tab.Content = template.LoadContent();
        }
    }
}
