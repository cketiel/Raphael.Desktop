using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>Which funding source to copy the rates from, and from which date (RE-072).</summary>
    public partial class CopyRatesViewModel : ObservableObject
    {
        private static string L(string key) => LocalizationService.Instance[key];

        public CopyRatesViewModel(List<FundingSource> sources, string targetName)
        {
            Sources = sources;
            TextIntro = string.Format(L("rates.CopyIntro"), targetName?.Trim());
        }

        public List<FundingSource> Sources { get; }
        public string TextIntro { get; }
        public string TextTitle => L("rates.CopyFrom");
        public string TextSource => L("rates.CopySource");
        public string TextFromDate => L("rates.CopyFromDate");
        public string TextCopy => L("rates.CopyButton");
        public string TextCancel => L("Cancel");

        [ObservableProperty] private FundingSource? _source;
        [ObservableProperty] private DateTime _fromDate = DateTime.Today;
        [ObservableProperty] private string _error = "";

        public event Action<bool>? RequestClose;

        [RelayCommand]
        private void Copy()
        {
            if (Source == null)
            {
                Error = L("rates.CopyPickSource");
                return;
            }

            RequestClose?.Invoke(true);
        }

        [RelayCommand]
        private void Cancel() => RequestClose?.Invoke(false);

        partial void OnSourceChanged(FundingSource? value) => Error = "";
    }
}
