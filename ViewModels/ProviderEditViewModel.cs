using Raphael.Desktop.Models;
using Raphael.Desktop.Commands;
using System.Windows;
using System.Windows.Input;
using System;

namespace Raphael.Desktop.ViewModels
{
    public class ProviderEditViewModel : BaseViewModel
    {
        public Provider Provider { get; set; }

        private string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                SetProperty(ref _selectedImagePath, value);
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        // If there is a local image, it shows the local one. If not, it shows the one from the server.
        public object PreviewImage
        {
            get
            {
                if (!string.IsNullOrEmpty(SelectedImagePath))
                    return SelectedImagePath; // Shows the selected local image

                return Provider.FullLogoUrl; // Show the image that is already on the server
            }
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand SelectLogoCommand { get; }

        public ProviderEditViewModel(Provider provider)
        {
            Provider = provider;
            SaveCommand = new RelayCommandObject(Save);
            CancelCommand = new RelayCommandObject(Cancel);
            SelectLogoCommand = new RelayCommandObject(_ => SelectLogo());
        }

        private void SelectLogo()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedImagePath = openFileDialog.FileName;
            }
        }

        private void Save(object obj)
        {
            if (string.IsNullOrWhiteSpace(Provider.Name))
            {
                MessageBox.Show("Company Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (obj is Window window) window.DialogResult = true;
        }

        private void Cancel(object obj)
        {
            if (obj is Window window) window.DialogResult = false;
        }
    }
}