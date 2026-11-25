using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using static Raphael.Desktop.Views.Reports.CalcUi;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>
    /// RE-092: saving a report's file. Covers the app while it is written, says what is happening and can be
    /// cancelled; then says how it ended and asks whether to open it. It never opens it on its own.
    /// </summary>
    /// <remarks>
    /// The file is written beside its destination, under a temporary name, and takes its place only when it is whole:
    /// cancelling, or an error, never leaves half a file nor damages the one that was there. Cancelling
    /// frees the screen at once, even if the writer never answers.
    /// </remarks>
    public sealed class SaveFileOverlay : Window
    {
        private readonly string _path;
        private readonly Action<string> _write;
        private readonly StackPanel _body = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _saving = true;

        private SaveFileOverlay(Window owner, string path, Action<string> write)
        {
            _path = path;
            _write = write;
            Owner = owner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromArgb(0x8C, 0x1E, 0x14, 0x38));
            FontFamily = owner.FontFamily;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Title = Path.GetFileName(path);

            // Exactly over the app, maximized or not.
            if (owner.Content is FrameworkElement area && PresentationSource.FromVisual(area)?.CompositionTarget is { } target)
            {
                var corner = target.TransformFromDevice.Transform(area.PointToScreen(new Point(0, 0)));
                (Left, Top, Width, Height) = (corner.X, corner.Y, area.ActualWidth, area.ActualHeight);
            }
            else WindowStartupLocation = WindowStartupLocation.CenterOwner;

            Content = new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(34, 28, 34, 26), Width = 460,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 3, Direction = 270, Opacity = 0.35 }, Child = _body
            };

            ShowSaving();
            Loaded += async (_, _) => await SaveAsync();
            Closing += (_, _) => _cts.Cancel();
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        /// <summary>Writes the file with the app covered. <paramref name="write"/> gets the path to write to, off the screen's thread.</summary>
        public static void Run(string path, Action<string> write)
        {
            var owner = Application.Current?.MainWindow;
            if (owner is null) { write(path); return; }
            new SaveFileOverlay(owner, path, write).ShowDialog();
        }

        private async Task SaveAsync()
        {
            var temp = Path.Combine(Path.GetDirectoryName(_path)!, $"~{Guid.NewGuid():N}{Path.GetExtension(_path)}");
            var token = _cts.Token;
            var work = Task.Run(() =>
            {
                try
                {
                    _write(temp);
                    token.ThrowIfCancellationRequested();
                    File.Move(temp, _path, true);
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                }
            });

            try
            {
                await work.WaitAsync(token);
                _saving = false;
                ShowSaved();
            }
            catch (OperationCanceledException)
            {
                // The screen is free already; whatever the writer ends in is not news.
                _ = work.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception ex)
            {
                _saving = false;
                FileLogger.Log($"Report file: {ex.GetType().Name}: {ex.Message}");
                // Excel or the reader keeps the old one open: the commonest of all.
                ShowError(ex is IOException or UnauthorizedAccessException ? T("save.InUse") : T("save.Error"));
            }
        }

        private void ShowSaving()
        {
            _body.Children.Clear();
            var ring = new ProgressBar { IsIndeterminate = true, Width = 54, Height = 54, HorizontalAlignment = HorizontalAlignment.Center, Foreground = WarningLook.Of(Violet) };
            if (Application.Current?.TryFindResource("MaterialDesignCircularProgressBar") is Style style) ring.Style = style;
            _body.Children.Add(ring);
            _body.Children.Add(Text(T("save.Saving"), 17, Deep, true, TextAlignment.Center, new Thickness(0, 16, 0, 4)));
            _body.Children.Add(Text(Path.GetFileName(_path), 13, Mute, false, TextAlignment.Center));
            _body.Children.Add(Buttons((T("save.Cancel"), PackIconKind.Close, false, Close)));
        }

        private void ShowSaved()
        {
            if (!IsVisible) return;
            _body.Children.Clear();
            _body.Children.Add(Mark(PackIconKind.CheckCircle, Green));
            _body.Children.Add(Text(T("save.Done"), 17, Deep, true, TextAlignment.Center, new Thickness(0, 12, 0, 4)));
            _body.Children.Add(Text(Path.GetFileName(_path), 13, Ink, true, TextAlignment.Center));
            _body.Children.Add(Text(Path.GetDirectoryName(_path) ?? "", 12, Mute, false, TextAlignment.Center, new Thickness(0, 2, 0, 0)));
            _body.Children.Add(Text(T("save.Open"), 14, Ink, false, TextAlignment.Center, new Thickness(0, 18, 0, 0)));
            _body.Children.Add(Buttons((T("save.Yes"), PackIconKind.FileEyeOutline, true, OpenFile), (T("save.No"), PackIconKind.Close, false, Close)));
        }

        private void ShowError(string message)
        {
            if (!IsVisible) return;
            _body.Children.Clear();
            _body.Children.Add(Mark(PackIconKind.AlertCircle, "#B3261E"));
            _body.Children.Add(Text(T("save.Failed"), 17, "#B3261E", true, TextAlignment.Center, new Thickness(0, 12, 0, 4)));
            _body.Children.Add(Text(Path.GetFileName(_path), 13, Ink, true, TextAlignment.Center));
            _body.Children.Add(Text(message, 13, Mute, false, TextAlignment.Center, new Thickness(0, 8, 0, 0)));
            _body.Children.Add(Buttons((T("save.Close"), PackIconKind.Close, false, Close)));
        }

        private void OpenFile()
        {
            if (_saving) return;
            Close();
            try { Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true }); }
            catch (Exception ex) { FileLogger.Log($"Report file: could not be opened. {ex.Message}"); }
        }

        private static PackIcon Mark(PackIconKind kind, string color) =>
            new() { Kind = kind, Width = 54, Height = 54, Foreground = WarningLook.Of(color), HorizontalAlignment = HorizontalAlignment.Center };

        private static StackPanel Buttons(params (string Text, PackIconKind Icon, bool Main, Action Act)[] items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
            foreach (var i in items)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(new PackIcon { Kind = i.Icon, Width = 17, Height = 17, VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(new TextBlock { Text = i.Text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Content = content, Width = 140, Height = 36, Margin = new Thickness(7, 0, 7, 0), IsDefault = i.Main };
                if (Application.Current?.TryFindResource(i.Main ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton") is Style style) b.Style = style;
                b.Background = i.Main ? WarningLook.Of(Violet) : Brushes.Transparent;
                b.Foreground = i.Main ? Brushes.White : WarningLook.Of(Violet);
                b.BorderBrush = i.Main ? WarningLook.Of(Violet) : WarningLook.Of("#B39DDB");
                b.Click += (_, _) => i.Act();
                row.Children.Add(b);
            }
            return row;
        }
    }
}
