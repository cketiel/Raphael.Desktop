using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Models
{
    /// <summary>
    /// One tickable line of a report's multi-select filter (a driver, a vehicle).
    /// </summary>
    /// <remarks>
    /// Exposes <c>Name</c> and <c>IsSelected</c> because that is what the report's
    /// MultiSelectComboBoxItemStyle binds to, so the models shared with other screens stay untouched.
    /// </remarks>
    public class ReportFilterOption : BaseViewModel
    {
        public ReportFilterOption(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }
}
