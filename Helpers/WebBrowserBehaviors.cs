using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Raphael.Desktop.Helpers
{
    public static class WebBrowserBehaviors
    {
        // Define a Dependency Property to allow binding a string to the WebBrowser Source
        public static readonly DependencyProperty SourceUriProperty =
            DependencyProperty.RegisterAttached("SourceUri", typeof(string), typeof(WebBrowserBehaviors),
                new PropertyMetadata(OnSourceUriChanged));

        public static string GetSourceUri(DependencyObject obj)
        {
            return (string)obj.GetValue(SourceUriProperty);
        }

        public static void SetSourceUri(DependencyObject obj, string value)
        {
            obj.SetValue(SourceUriProperty, value);
        }

        private static void OnSourceUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var browser = d as WebBrowser;
            if (browser == null) return;

            string uri = e.NewValue as string;
            if (!string.IsNullOrEmpty(uri))
            {
                try
                {
                    // Navigate to the file path or URL
                    browser.Navigate(new Uri(uri));
                }
                catch (Exception)
                {
                    // Handle potential URI errors
                }
            }
        }
    }
}
