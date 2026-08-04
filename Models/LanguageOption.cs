
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Models
{
    public class LanguageOption : BaseViewModel
    {
        public string LanguageCode { get; set; }
        public string DisplayName { get; set; }
        public string FlagEmoji { get; set; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CheckVisibility));
            }
        }

        public System.Windows.Visibility CheckVisibility => IsSelected ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }
}
