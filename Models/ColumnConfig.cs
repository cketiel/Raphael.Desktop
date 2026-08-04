
using CommunityToolkit.Mvvm.ComponentModel;

namespace Raphael.Desktop.Models
{
    public partial class ColumnConfig : ObservableObject
    {
        // Technical name of the property in the DTO, ex: "AuthNo"
        public string PropertyName { get; set; }

        // Title that the user sees in the grid, ex: "AuthNo" or "Authorization #"
        [ObservableProperty]
        private string _header;

        // Whether the column is visible or not
        [ObservableProperty]
        private bool _isVisible;
    }
}
