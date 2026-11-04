using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views.Compliance
{
    /// <summary>
    /// Shows the files of a compliance document (RE-065). Photos open here, all of a document in one
    /// window (the five vehicle photos side by side), so looking at them never depends on the photo
    /// app of the computer. A PDF opens in the computer's viewer; if there is none, its folder opens
    /// with the file selected. Nothing is kept: the files live in a temporary folder until the next.
    /// </summary>
    public static class ComplianceFileViewer
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public static async Task ShowAsync(ComplianceService service, ComplianceDocumentDto doc)
        {
            ComplianceService.ClearDownloads();
            var images = new List<(string Slot, string Path)>();
            // In the order the driver takes them: front, back, right, left, inside.
            string[] order = ["PAGE", "FRONT", "BACK", "RIGHT", "LEFT", "INTERIOR"];
            foreach (var file in doc.Files.OrderBy(f => Array.IndexOf(order, f.Slot)))
            {
                var path = await service.DownloadAsync(doc.Id, file.Slot, file.ContentType);
                if (file.ContentType == "application/pdf") OpenPdf(path);
                else images.Add((file.Slot, path));
            }
            if (images.Count > 0) ShowImages($"{doc.DocumentTypeName} · {doc.SubjectName}", images);
        }

        private static void OpenPdf(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                // No PDF viewer registered: the folder, with the file selected.
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
        }

        private static void ShowImages(string title, List<(string Slot, string Path)> images)
        {
            var panel = new WrapPanel { Margin = new Thickness(12) };
            var width = images.Count == 1 ? 900 : 420;
            foreach (var (slot, path) in images)
            {
                // Loaded into memory, so the temporary file is not held open.
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();

                var card = new StackPanel { Margin = new Thickness(8) };
                if (images.Count > 1)
                    card.Children.Add(new TextBlock { Text = T($"compliance.Slot.{slot}"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                card.Children.Add(new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)), BorderThickness = new Thickness(1),
                    Child = new Image { Source = bitmap, Width = width, Stretch = Stretch.Uniform }
                });
                panel.Children.Add(card);
            }
            new Window
            {
                Title = title,
                Width = images.Count == 1 ? 960 : 940,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current?.MainWindow,
                Background = Brushes.White,
                Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
            }.Show();
        }
    }
}
