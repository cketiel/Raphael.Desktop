using Raphael.Desktop.Models;
using Raphael.Desktop.Commands;
using System.Windows.Input;
using System.ComponentModel;
using System;
using System.Windows.Media;
using System.Diagnostics; 

namespace Raphael.Desktop.ViewModels
{
    public class AddEditGroupViewModel : BaseViewModel, IDataErrorInfo
    {
        private VehicleGroup _currentGroup;
        public VehicleGroup CurrentGroup
        {
            get => _currentGroup;
            private set 
            {
                _currentGroup = value;                
                OnPropertyChanged(nameof(Name));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(CurrentGroupColorString)); 
            }
        }

        private Color _selectedMediaColor;
        public Color SelectedMediaColor 
        {
            get => _selectedMediaColor;
            set
            {
                if (_selectedMediaColor != value)
                {
                    _selectedMediaColor = value;
                    OnPropertyChanged(nameof(SelectedMediaColor));

                    // Update the string property in the CurrentGroup model
                    if (CurrentGroup != null)
                    {
                        string newColorString = _selectedMediaColor.ToString(); // e.g., #AARRGGBB

                        // Only update CurrentGroup.Color if it has actually changed
                        // to avoid unnecessary notifications if the string was already the same.
                        if (CurrentGroup.Color != newColorString)
                        {
                            CurrentGroup.Color = newColorString;
                            Debug.WriteLine($"ColorPicker changed. New MediaColor: {_selectedMediaColor}, Updated CurrentGroup.Color: {CurrentGroup.Color}");
                            // Notify that the string property has changed, for the debug TextBlock
                            OnPropertyChanged(nameof(CurrentGroupColorString));
                        }
                    }
                }
            }
        }

        // Property to show the string of the current color of the CurrentGroup in the popup (for debugging)
        public string CurrentGroupColorString => CurrentGroup?.Color;


        // Traducciones...
        public string WindowTitle => (CurrentGroup?.Id ?? 0) == 0 ? Services.LocalizationService.Instance["AddGroupTitle"] : Services.LocalizationService.Instance["EditGroupTitle"];
        public string NameLabel => Services.LocalizationService.Instance["GroupNameText"];
        public string DescriptionLabel => Services.LocalizationService.Instance["Description"];
        public string ColorLabel => Services.LocalizationService.Instance["GroupColorText"];
        public string OkButtonText => Services.LocalizationService.Instance["OK"];
        public string CancelButtonText => Services.LocalizationService.Instance["Cancel"];

        public AddEditGroupViewModel(VehicleGroup groupToEdit = null)
        {
            CurrentGroup = groupToEdit ?? new VehicleGroup();          
            InitializeColorProperties();
        }

        private void InitializeColorProperties()
        {
            Color initialMediaColor;
            if (CurrentGroup != null && !string.IsNullOrEmpty(CurrentGroup.Color))
            {
                try
                {
                    initialMediaColor = (Color)ColorConverter.ConvertFromString(CurrentGroup.Color);
                    Debug.WriteLine($"Initializing. CurrentGroup.Color: '{CurrentGroup.Color}', Parsed to MediaColor: {initialMediaColor}");
                }
                catch (FormatException ex)
                {
                    Debug.WriteLine($"Error converting initial CurrentGroup.Color '{CurrentGroup.Color}' to MediaColor: {ex.Message}. Defaulting to Gray.");
                    initialMediaColor = Colors.Gray;
                    CurrentGroup.Color = initialMediaColor.ToString(); 
                }
            }
            else 
            {
                initialMediaColor = Colors.Gray; 
                if (CurrentGroup != null)
                {
                    CurrentGroup.Color = initialMediaColor.ToString(); 
                }
                Debug.WriteLine($"Initializing. CurrentGroup.Color was null/empty. Defaulted to MediaColor: {initialMediaColor}, CurrentGroup.Color set to: {CurrentGroup?.Color}");
            }
            
            _selectedMediaColor = initialMediaColor;
           
            OnPropertyChanged(nameof(SelectedMediaColor));
            
            OnPropertyChanged(nameof(CurrentGroupColorString));
        }


        // IDataErrorInfo...
        public string Error => null;
        public string this[string columnName]
        {
            get
            {
                string result = null;
                if (columnName == nameof(Name))
                {
                    if (string.IsNullOrWhiteSpace(CurrentGroup?.Name))
                        result = Services.LocalizationService.Instance["NameIsRequiredError"];
                }
                return result;
            }
        }

        // Name and Description properties (updated directly in CurrentGroup)
        public string Name
        {
            get => CurrentGroup?.Name;
            set
            {
                if (CurrentGroup != null && CurrentGroup.Name != value)
                {
                    CurrentGroup.Name = value;
                    OnPropertyChanged(nameof(Name)); // Required for UI validation and update
                }
            }
        }

        public string Description
        {
            get => CurrentGroup?.Description;
            set
            {
                if (CurrentGroup != null && CurrentGroup.Description != value)
                {
                    CurrentGroup.Description = value;
                    OnPropertyChanged(nameof(Description));
                }
            }
        }
    }
}