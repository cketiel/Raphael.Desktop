using System;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Models
{
    public class Provider : BaseViewModel, ICloneable
    {
        private int _id;
        public int Id { get => _id; set => SetProperty(ref _id, value); }

        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _address = string.Empty;
        public string Address { get => _address; set => SetProperty(ref _address, value); }

        private string _email = string.Empty;
        public string Email { get => _email; set => SetProperty(ref _email, value); }

        private string _phone = string.Empty;
        public string Phone { get => _phone; set => SetProperty(ref _phone, value); }

        private string _logo = string.Empty;
        public string Logo
        {
            get => _logo;
            set
            {
                if (SetProperty(ref _logo, value))
                    OnPropertyChanged(nameof(FullLogoUrl));
            }
        }

        private double? _latitude;
        public double? Latitude { get => _latitude; set => SetProperty(ref _latitude, value); }

        private double? _longitude;
        public double? Longitude { get => _longitude; set => SetProperty(ref _longitude, value); }

        public string FullLogoUrl
        {
            get
            {
                if (string.IsNullOrEmpty(Logo))
                    return "pack://application:,,,/Assets/no-image.png";
               
                string baseUrl = App.Configuration["ApiAddress:ApiTest"].TrimEnd('/');
               
                string logoPath = Logo.StartsWith("logos/") ? Logo : $"logos/{Logo}";

                return $"{baseUrl}/{logoPath}";
            }
        }

        public object Clone() => this.MemberwiseClone();
    }
}