using System.ComponentModel;
using System.Runtime.CompilerServices;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected BaseViewModel()
        {
            LocalizationService.Instance.LanguageChanged += () =>
            {
                OnPropertyChanged(string.Empty); // Notify all properties
            };
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
