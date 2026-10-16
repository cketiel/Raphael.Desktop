using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Draws an amber frame and a small padlock around a field that cannot be changed (RE-051).
    /// </summary>
    /// <remarks>
    /// An adorner and not a wrapping Border on purpose: the trip form is a grid of fixed rows, and
    /// wrapping each locked control would move every one of them. The frame is the same amber as the
    /// banner that explains it, which is how the two are read as one message.
    ///
    /// <para>
    /// ⚠️ The form's tabs take their content out of the visual tree when they are not selected, and
    /// an adorner left behind would paint over whatever the next tab puts there. It is added on
    /// Loaded and removed on Unloaded, never just once.
    /// </para>
    /// </remarks>
    public static class LockHighlight
    {
        public static readonly DependencyProperty IsOnProperty =
            DependencyProperty.RegisterAttached("IsOn", typeof(bool), typeof(LockHighlight),
                new PropertyMetadata(false, OnIsOnChanged));

        public static bool GetIsOn(DependencyObject d) => (bool)d.GetValue(IsOnProperty);
        public static void SetIsOn(DependencyObject d, bool value) => d.SetValue(IsOnProperty, value);

        private static readonly DependencyProperty AdornerProperty =
            DependencyProperty.RegisterAttached("Adorner", typeof(FrameAdorner), typeof(LockHighlight));

        private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element) return;

            element.Loaded -= OnLoaded;
            element.Unloaded -= OnUnloaded;

            if ((bool)e.NewValue)
            {
                element.Loaded += OnLoaded;
                element.Unloaded += OnUnloaded;
                if (element.IsLoaded) Attach(element);
            }
            else
            {
                Detach(element);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((FrameworkElement)sender);
        private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((FrameworkElement)sender);

        private static void Attach(FrameworkElement element)
        {
            if (element.GetValue(AdornerProperty) != null) return;
            var layer = AdornerLayer.GetAdornerLayer(element);
            if (layer == null) return;

            var adorner = new FrameAdorner(element);
            layer.Add(adorner);
            element.SetValue(AdornerProperty, adorner);
        }

        private static void Detach(FrameworkElement element)
        {
            if (element.GetValue(AdornerProperty) is not FrameAdorner adorner) return;
            AdornerLayer.GetAdornerLayer(element)?.Remove(adorner);
            element.ClearValue(AdornerProperty);
        }

        private sealed class FrameAdorner : Adorner
        {
            private static readonly Pen Frame = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)), 2));
            private static readonly Brush Badge = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));

            // A padlock, drawn rather than loaded: the adorner layer has no access to the icon font.
            private static readonly Geometry Padlock = Geometry.Parse(
                "M3,5 L3,3.5 A2.5,2.5 0 0 1 8,3.5 L8,5 L9,5 L9,10 L2,10 L2,5 Z M4.2,5 L6.8,5 L6.8,3.5 A1.3,1.3 0 0 0 4.2,3.5 Z");

            public FrameAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

            protected override void OnRender(DrawingContext dc)
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;

                dc.DrawRoundedRectangle(null, Frame, new Rect(-2, -2, size.Width + 4, size.Height + 4), 4, 4);

                var badge = new Rect(size.Width - 9, -7, 14, 14);
                dc.DrawEllipse(Badge, null, new Point(badge.X + 7, badge.Y + 7), 7, 7);
                dc.PushTransform(new TranslateTransform(badge.X + 1.5, badge.Y + 1.5));
                dc.DrawGeometry(Brushes.White, null, Padlock);
                dc.Pop();
            }

            private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
        }
    }
}
