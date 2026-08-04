using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace Raphael.Desktop.ViewModels
{
    public partial class ScheduleColumnSelectorViewModel : ObservableObject
    {
        public string Title => "DataGrid Layout";

        public ObservableCollection<ColumnConfig> Columns { get; }

        // The result will be used to know if the user pressed OK.
        public bool? DialogResult { get; private set; }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        // The action that closes the window, we will pass it to you from the view.
        private Action _closeAction;

        public ScheduleColumnSelectorViewModel(IEnumerable<ColumnConfig> currentConfig, Action closeAction)
        {
            // A copy of the configuration is created so that changes can be canceled.
            Columns = new ObservableCollection<ColumnConfig>(
                currentConfig.Select(c => new ColumnConfig
                {
                    PropertyName = c.PropertyName,
                    Header = c.Header,
                    IsVisible = c.IsVisible
                })
            );

            _closeAction = closeAction;
            OkCommand = new RelayCommand(AcceptChanges);
            CancelCommand = new RelayCommand(CancelChanges);
        }

        private void AcceptChanges()
        {
            DialogResult = true;
            _closeAction?.Invoke();
        }

        private void CancelChanges()
        {
            DialogResult = false;
            _closeAction?.Invoke();
        }
    }
}