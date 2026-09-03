using GMap.NET;
using GMap.NET.WindowsPresentation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Raphael.Desktop.Helpers.Maps
{
    /// <summary>
    /// Places the markers of an <see cref="ItemsControl"/> on a <see cref="GMapControl"/>.
    ///
    /// It replaces two seven-value MultiBindings per marker — one for Canvas.Left, one for
    /// Canvas.Top, both listening to the map's Zoom and Position. With those, a single mouse
    /// wheel tick re-ran the coordinate conversion fourteen times per marker, and the whole
    /// route is on screen at once. Here one pass over the layer positions every marker, and
    /// the pass runs when the map actually moved.
    ///
    /// Usage, on the ItemsControl whose ItemsPanel is a Canvas:
    /// <code>
    /// maps:MapMarkerLayer.Map="{Binding ElementName=MapView}"
    /// maps:MapMarkerLayer.RefreshTick="{Binding MapRefreshTick}"
    /// </code>
    /// The items must implement <see cref="IMapMarker"/>. Bumping <c>RefreshTick</c> from the
    /// view model forces a pass — for when the data moved but the map did not.
    /// </summary>
    public static class MapMarkerLayer
    {
        /// <summary>Pixels each overlapping marker is pushed down, so they stay countable.</summary>
        private const double VerticalOffsetAmount = 22.0;

        #region Map

        public static readonly DependencyProperty MapProperty =
            DependencyProperty.RegisterAttached(
                "Map",
                typeof(GMapControl),
                typeof(MapMarkerLayer),
                new PropertyMetadata(null, OnMapChanged));

        public static GMapControl GetMap(DependencyObject element) =>
            (GMapControl)element.GetValue(MapProperty);

        public static void SetMap(DependencyObject element, GMapControl value) =>
            element.SetValue(MapProperty, value);

        #endregion

        #region RefreshTick

        public static readonly DependencyProperty RefreshTickProperty =
            DependencyProperty.RegisterAttached(
                "RefreshTick",
                typeof(int),
                typeof(MapMarkerLayer),
                new PropertyMetadata(0, OnRefreshTickChanged));

        public static int GetRefreshTick(DependencyObject element) =>
            (int)element.GetValue(RefreshTickProperty);

        public static void SetRefreshTick(DependencyObject element, int value) =>
            element.SetValue(RefreshTickProperty, value);

        #endregion

        // One attachment per layer, keyed by the ItemsControl it drives. A weak table and not
        // a dictionary: this is static and lives as long as the process, and the Schedule tab
        // is opened and closed all shift. Holding the layers strongly here would leak one
        // screen's worth of visual tree on every open.
        private static readonly ConditionalWeakTable<ItemsControl, Attachment> Attachments = new();

        private static void OnMapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ItemsControl layer) return;

            if (Attachments.TryGetValue(layer, out var existing))
            {
                existing.Dispose();
                Attachments.Remove(layer);
            }

            if (e.NewValue is GMapControl map)
            {
                var attachment = new Attachment(layer, map);
                Attachments.Add(layer, attachment);
                attachment.Start();
            }
        }

        private static void OnRefreshTickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ItemsControl layer && Attachments.TryGetValue(layer, out var attachment))
                attachment.PlaceAll();
        }

        private sealed class Attachment
        {
            private readonly ItemsControl _layer;
            private readonly GMapControl _map;
            private readonly DependencyPropertyDescriptor _zoom;
            private readonly DependencyPropertyDescriptor _position;
            private bool _watching;

            public Attachment(ItemsControl layer, GMapControl map)
            {
                _layer = layer;
                _map = map;
                _zoom = DependencyPropertyDescriptor.FromProperty(GMapControl.ZoomProperty, typeof(GMapControl));
                _position = DependencyPropertyDescriptor.FromProperty(GMapControl.PositionProperty, typeof(GMapControl));
            }

            /// <summary>Begins watching, and keeps watching across tab switches.</summary>
            public void Start()
            {
                _layer.Loaded += OnLayerLoaded;
                _layer.Unloaded += OnLayerUnloaded;

                Subscribe();
            }

            /// <summary>Stops for good: the layer is being replaced or thrown away.</summary>
            public void Dispose()
            {
                _layer.Loaded -= OnLayerLoaded;
                _layer.Unloaded -= OnLayerUnloaded;

                Unsubscribe();
            }

            private void Subscribe()
            {
                if (_watching) return;
                _watching = true;

                // Zoom and Position rather than the control's own events: those two are the
                // dependency properties the old bindings listened to, so watching them keeps
                // the exact same trigger set — wheel, drag, and SetZoomToFitRect alike.
                _zoom?.AddValueChanged(_map, OnMapMoved);
                _position?.AddValueChanged(_map, OnMapMoved);
                _map.SizeChanged += OnMapResized;

                _layer.ItemContainerGenerator.StatusChanged += OnContainersChanged;

                PlaceAll();
            }

            private void Unsubscribe()
            {
                if (!_watching) return;
                _watching = false;

                _zoom?.RemoveValueChanged(_map, OnMapMoved);
                _position?.RemoveValueChanged(_map, OnMapMoved);
                _map.SizeChanged -= OnMapResized;

                _layer.ItemContainerGenerator.StatusChanged -= OnContainersChanged;
            }

            // Unloaded fires on a plain tab switch, not only on a real close, so the watch is
            // released and taken again rather than ended. A layer that unsubscribed for good
            // here would come back to a screen whose markers no longer follow the map.
            private void OnLayerLoaded(object sender, RoutedEventArgs e) => Subscribe();

            private void OnLayerUnloaded(object sender, RoutedEventArgs e) => Unsubscribe();

            private void OnMapMoved(object sender, EventArgs e) => PlaceAll();

            private void OnMapResized(object sender, SizeChangedEventArgs e) => PlaceAll();

            private void OnContainersChanged(object sender, EventArgs e)
            {
                if (_layer.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;

                PlaceAll();

                // And once more after layout: a container that has just been generated has not
                // been measured yet, so the first pass centres it on a size of zero. The second
                // pass, at Loaded priority, runs when the sizes are real.
                _layer.Dispatcher.BeginInvoke(new Action(PlaceAll), DispatcherPriority.Loaded);
            }

            public void PlaceAll()
            {
                var generator = _layer.ItemContainerGenerator;
                if (generator.Status != GeneratorStatus.ContainersGenerated) return;

                for (var i = 0; i < _layer.Items.Count; i++)
                {
                    if (_layer.Items[i] is not IMapMarker marker) continue;
                    if (generator.ContainerFromIndex(i) is not FrameworkElement container) continue;

                    Place(container, marker);
                }
            }

            private void Place(FrameworkElement container, IMapMarker marker)
            {
                GPoint point;
                try
                {
                    point = _map.FromLatLngToLocal(new PointLatLng(marker.MarkerLatitude, marker.MarkerLongitude));
                }
                catch (Exception ex)
                {
                    // The map answers with its own projection; if it is not ready it throws
                    // rather than returning something wrong. Skipping one marker for one pass
                    // is better than tearing down the layer.
                    System.Diagnostics.Debug.WriteLine($"MapMarkerLayer: {ex.Message}");
                    return;
                }

                // A container that has never been measured reports zero, which would pin the
                // marker's corner to the coordinate instead of its centre. DesiredSize is what
                // the template asked for and is available before the first arrange.
                var width = container.ActualWidth > 0 ? container.ActualWidth : container.DesiredSize.Width;
                var height = container.ActualHeight > 0 ? container.ActualHeight : container.DesiredSize.Height;

                Canvas.SetLeft(container, point.X - (width / 2));
                Canvas.SetTop(container, point.Y - (height / 2) + (marker.MarkerOffsetIndex * VerticalOffsetAmount));
            }
        }
    }
}
