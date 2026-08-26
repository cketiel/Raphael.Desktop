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

        private string? _timeZoneId;

        /// <summary>
        /// The timezone this provider's trips are operated in. IANA identifier.
        /// </summary>
        /// <remarks>
        /// This is what a pickup time means. A trip at 09:15 is 09:15 here, whoever opens
        /// the screen and wherever the API happens to be hosted.
        ///
        /// <para>
        /// Empty until somebody fills it in, and then the server falls back to its
        /// configured default. The list flags the ones still empty so the fallback does not
        /// become permanent by inattention.
        /// </para>
        /// </remarks>
        public string? TimeZoneId
        {
            get => _timeZoneId;
            set
            {
                if (SetProperty(ref _timeZoneId, value))
                    OnPropertyChanged(nameof(HasTimeZone));
            }
        }

        public bool HasTimeZone => !string.IsNullOrWhiteSpace(TimeZoneId);

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