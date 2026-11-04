using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Compliance
{
    /// <summary>
    /// Uploads, renews or corrects one compliance document of a person or a vehicle (RE-065).
    /// <list type="bullet">
    /// <item>New or renewal: a file per part (one page, or the five vehicle photos) and the expiry.
    /// It waits for approval; an administrator may approve it as it is saved.</item>
    /// <item>Correction (<c>editing</c> given): a document still waiting gets another expiry, or any
    /// of its files replaced. An approved one is never changed here: it is renewed.</item>
    /// </list>
    /// </summary>
    public sealed class ComplianceUploadDialog : Window
    {
        private const long MaxBytes = 10 * 1024 * 1024;
        private static string T(string key) => LocalizationService.Instance[key];
        private static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));

        private readonly ComplianceService _service;
        private readonly ComplianceDocumentTypeDto _type;
        private readonly ComplianceSubjectDto _owner;
        private readonly ComplianceDocumentDto _editing;
        private readonly Dictionary<string, string> _files = new();
        private readonly Dictionary<string, TextBlock> _names = new();
        private readonly DatePicker _date = new() { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        private readonly CheckBox _approve = new() { Margin = new Thickness(0, 14, 0, 0) };
        private readonly TextBlock _error = new() { Foreground = Brushes.OrangeRed, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        private readonly TextBlock _progress = new() { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
        private readonly ProgressBar _bar = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        private readonly Button _send = new() { IsDefault = true, Padding = new Thickness(18, 4, 18, 4) };
        private readonly Button _cancel = new() { IsCancel = true, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(18, 4, 18, 4) };

        public ComplianceUploadDialog(ComplianceService service, ComplianceDocumentTypeDto type, ComplianceSubjectDto owner, ComplianceDocumentDto editing)
        {
            _service = service;
            _type = type;
            _owner = owner;
            _editing = editing;
            Title = T(editing is null ? "compliance.UploadTitle" : "compliance.EditTitle");
            Width = 580;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current?.MainWindow;
            Background = Brushes.White;

            var root = new StackPanel { Margin = new Thickness(24) };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new PackIcon { Kind = editing is null ? PackIconKind.FileUploadOutline : PackIconKind.FileEditOutline, Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = Blue });
            head.Children.Add(new TextBlock { Text = type.Name, FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(head);
            root.Children.Add(new TextBlock { Text = owner.SubjectName, Opacity = 0.7, Margin = new Thickness(38, 0, 0, 14) });

            var filesBox = new GroupBox { Header = T(editing is null ? "compliance.FilesHeader" : "compliance.FilesReplaceHeader"), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(10) };
            var filesPanel = new StackPanel();
            filesPanel.Children.Add(new TextBlock { Text = T("compliance.UploadHint"), Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6) });
            foreach (var slot in Slots)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = T($"compliance.Slot.{slot}"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
                var pick = new Button { Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton"), Padding = new Thickness(12, 2, 12, 2) };
                pick.Content = Labelled(PackIconKind.Paperclip, T("compliance.ChooseFile"));
                var current = editing?.Files.FirstOrDefault(f => f.Slot == slot);
                var name = new TextBlock
                {
                    Text = current is null ? T("compliance.NoFile") : string.Format(T("compliance.KeepsFile"), current.ContentType == "application/pdf" ? "PDF" : T("compliance.Image")),
                    Opacity = 0.6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                };
                var s = slot;
                pick.Click += (_, _) => Pick(s);
                Grid.SetColumn(pick, 1);
                Grid.SetColumn(name, 2);
                row.Children.Add(pick);
                row.Children.Add(name);
                _names[slot] = name;
                filesPanel.Children.Add(row);
            }
            filesBox.Content = filesPanel;
            root.Children.Add(filesBox);

            root.Children.Add(new TextBlock { Text = T("compliance.ExpiresOn"), FontWeight = FontWeights.SemiBold });
            _date.DisplayDateStart = DateTime.Today;
            if (editing != null) _date.SelectedDate = editing.ExpiresOn;
            root.Children.Add(_date);

            // An administrator has just looked at it: they may make it count now. Recorded as approved by them.
            if (ComplianceViewModel.IsAdminUser)
            {
                _approve.Content = T("compliance.ApproveOnSave");
                _approve.IsChecked = true;
                root.Children.Add(_approve);
            }

            root.Children.Add(_error);
            root.Children.Add(_bar);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            _cancel.Content = T("compliance.Cancel");
            _cancel.Style = (Style)Application.Current.FindResource("MaterialDesignOutlinedButton");
            _send.Content = Labelled(PackIconKind.ContentSave, T(editing is null ? "compliance.Save" : "compliance.SaveChanges"));
            _send.Click += async (_, _) => await SaveAsync();
            buttons.Children.Add(_progress);
            buttons.Children.Add(_cancel);
            buttons.Children.Add(_send);
            root.Children.Add(buttons);
            Content = root;
        }

        private List<string> Slots => _type.Slots.DefaultIfEmpty("PAGE").ToList();

        private static StackPanel Labelled(PackIconKind icon, string text)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(new PackIcon { Kind = icon, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0) });
            return p;
        }

        private void Pick(string slot)
        {
            var dialog = new OpenFileDialog { Filter = "JPG, PNG, PDF|*.jpg;*.jpeg;*.png;*.pdf" };
            if (dialog.ShowDialog(this) != true) return;
            if (new FileInfo(dialog.FileName).Length > MaxBytes)
            {
                _error.Text = T("compliance.FileTooBig");
                return;
            }
            _error.Text = string.Empty;
            _files[slot] = dialog.FileName;
            _names[slot].Text = Path.GetFileName(dialog.FileName);
            _names[slot].Opacity = 1;
        }

        private async System.Threading.Tasks.Task SaveAsync()
        {
            if (_editing is null && Slots.Any(s => !_files.ContainsKey(s))) { _error.Text = T("compliance.MissingFiles"); return; }
            if (_date.SelectedDate is not DateTime expires) { _error.Text = T("compliance.ChooseDate"); return; }
            if (expires.Date < DateTime.Today) { _error.Text = T("compliance.DateInPast"); return; }

            _error.Text = string.Empty;
            _send.IsEnabled = _cancel.IsEnabled = false;
            _bar.Visibility = Visibility.Visible;
            var chosen = Slots.Where(_files.ContainsKey).Select(s => (s, _files[s])).ToList();
            var progress = new Progress<int>(sent => _progress.Text = string.Format(T("compliance.Uploading"), sent + 1, chosen.Count));
            try
            {
                int id;
                string error;
                if (_editing is null)
                {
                    (id, error) = await _service.UploadAsync(new ComplianceDocumentCreateDto
                    {
                        DocumentTypeId = _type.Id,
                        Subject = _owner.Subject,
                        SubjectId = _owner.SubjectId,
                        ExpiresOn = DateTime.SpecifyKind(expires.Date, DateTimeKind.Unspecified)
                    }, chosen, progress);
                }
                else
                {
                    id = _editing.Id;
                    error = expires.Date != _editing.ExpiresOn.Date ? await _service.UpdateExpiryAsync(id, expires) : null;
                    if (error is null && chosen.Count > 0) error = await _service.PutFilesAsync(id, chosen, progress);
                }
                if (error is null && _approve.IsChecked == true && ComplianceViewModel.IsAdminUser)
                {
                    _progress.Text = T("compliance.Approving");
                    error = await _service.ApproveAsync(id);
                }

                if (error != null)
                {
                    _error.Text = error;
                    return;
                }
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _error.Text = ex.Message;
            }
            finally
            {
                _send.IsEnabled = _cancel.IsEnabled = true;
                _bar.Visibility = Visibility.Collapsed;
                _progress.Text = string.Empty;
            }
        }
    }
}
