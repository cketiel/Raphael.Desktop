using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views.Compliance
{
    /// <summary>
    /// Every version of one document of a person or a vehicle (RE-065): the one that counts, the ones
    /// it replaced, the rejected ones, with who uploaded and who reviewed each. An approved document
    /// is never edited, so this is what was approved, as it was.
    /// </summary>
    public sealed class ComplianceHistoryDialog : Window
    {
        private static string T(string key) => LocalizationService.Instance[key];

        public sealed class Line
        {
            public ComplianceDocumentDto Doc { get; init; }
            public string Status { get; init; }
            public string Expires => Doc.ExpiresOn.ToString("MM/dd/yyyy");
            public string Uploaded => $"{Doc.UploadedAtUtc.ToLocalTime():MM/dd/yyyy h:mm tt} · {Doc.UploadedByName}";
            public string Reviewed => Doc.ReviewedAtUtc is null ? "—" : $"{Doc.ReviewedAtUtc.Value.ToLocalTime():MM/dd/yyyy} · {Doc.ReviewedByName}";
            public string Files => ComplianceUi.FilesText(Doc);
            public string Reason => Doc.RejectionReason;
        }

        public ComplianceHistoryDialog(ComplianceService service, string title, List<ComplianceDocumentDto> versions)
        {
            Title = T("compliance.HistoryTitle");
            Width = 980;
            Height = 460;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current?.MainWindow;
            Background = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new PackIcon { Kind = PackIconKind.History, Width = 26, Height = 26, Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)), VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var grid = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
                // The trip grids' look, as in Docs.
                Style = (Style)Application.Current.FindResource("ContentDataGridStyle"),
                AlternationCount = 2,
                ItemsSource = versions.Select(v => new Line { Doc = v, Status = StatusText(v.Status) }).ToList()
            };

            // View: the same eye as in Docs.
            var view = new FrameworkElementFactory(typeof(Button));
            view.SetValue(StyleProperty, Application.Current.FindResource("TripActionButtonStyle"));
            view.SetValue(ToolTipProperty, T("compliance.View"));
            var eye = new FrameworkElementFactory(typeof(PackIcon));
            eye.SetValue(PackIcon.KindProperty, PackIconKind.EyeOutline);
            eye.SetValue(ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)));
            view.AppendChild(eye);
            view.AddHandler(Button.ClickEvent, new RoutedEventHandler(async (s, _) =>
            {
                if ((s as FrameworkElement)?.DataContext is not Line line) return;
                try
                {
                    await ComplianceFileViewer.ShowAsync(service, line.Doc);
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
            }));
            grid.Columns.Add(new DataGridTemplateColumn { Width = DataGridLength.Auto, CellTemplate = new DataTemplate { VisualTree = view } });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Status"), Binding = new Binding(nameof(Line.Status)), Width = new DataGridLength(110) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Expires"), Binding = new Binding(nameof(Line.Expires)), Width = new DataGridLength(100) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Files"), Binding = new Binding(nameof(Line.Files)), Width = new DataGridLength(80) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Uploaded"), Binding = new Binding(nameof(Line.Uploaded)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Reviewed"), Binding = new Binding(nameof(Line.Reviewed)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = T("compliance.Reason"), Binding = new Binding(nameof(Line.Reason)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            root.Children.Add(grid);
            Content = root;
        }

        private static string StatusText(string status) => status switch
        {
            "APPROVED" => T("compliance.Approved"),
            "PENDING" => T("compliance.InReview"),
            "REJECTED" => T("compliance.Rejected"),
            "SUPERSEDED" => T("compliance.Superseded"),
            "WITHDRAWN" => T("compliance.Withdrawn"),
            _ => status
        };
    }
}
