using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>One card of "what it carries": AMB, WCH or STR.</summary>
    public class VehicleKindCard : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private readonly VehicleEditViewModel _owner;

        public VehicleKindCard(VehicleEditViewModel owner, int level, List<VehicleConfigurationDto> configurations)
        {
            _owner = owner;
            Level = level;
            Configurations = configurations;
        }

        public int Level { get; }
        public string Kind => VehicleCapabilityUi.Kind(Level)!;
        public List<VehicleConfigurationDto> Configurations { get; }
        public bool HasConfigurations => Configurations.Count > 0;

        public string Title => LocalizationService.Instance["vehicle.Kind" + Kind];
        public string Carries => LocalizationService.Instance["vehicle.Carries" + Kind];

        /// <summary>The seats of the configuration this card would give, or why it cannot be chosen.</summary>
        public string Limits => HasConfigurations
            ? (IsSelected ? _owner.Configuration ?? Configurations[0] : Configurations[0]).SeatsText
            : LocalizationService.Instance["vehicle.NoConfigurationOfKind"];

        public bool IsSelected
        {
            get => _owner.Configuration?.Level == Level;
            set { if (value && !IsSelected && HasConfigurations) _owner.Configuration = Configurations[0]; }
        }

        public void Raise() => OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// A vehicle's form. RE-083: what it carries is a card (AMB, WCH, STR), and the card gives it one
    /// of the configurations of that kind; which one is asked only when there are several.
    /// </summary>
    public class VehicleEditViewModel : BaseViewModel
    {
        public LocalizationService L => LocalizationService.Instance;

        public Vehicle Vehicle { get; }
        public string Title { get; }
        public string HeaderText => string.IsNullOrWhiteSpace(Vehicle.Name) ? Title : Vehicle.Name;

        public ObservableCollection<VehicleGroup> AllGroups { get; }
        public ObservableCollection<CapacityDetailType> AllCapacityTypes { get; }
        public ObservableCollection<VehicleType> AllVehicleTypes { get; }

        public List<VehicleKindCard> Cards { get; } = new();
        public bool HasCards => Cards.Count > 0;
        public bool ShowPlainList => !HasCards;

        private VehicleConfigurationDto? _configuration;
        public VehicleConfigurationDto? Configuration
        {
            get => _configuration;
            set
            {
                // The combo clears its selection while its list changes: that is not a choice.
                if (value is null || ReferenceEquals(value, _configuration)) return;
                _configuration = value;
                Vehicle.CapacityDetailTypeId = value.Id;
                RaiseChoice();
            }
        }

        /// <summary>The configurations of the chosen kind.</summary>
        public List<VehicleConfigurationDto> Configurations => Cards.FirstOrDefault(c => c.IsSelected)?.Configurations ?? new();
        public bool ShowConfigurations => Configurations.Count > 1;

        public string? SelectedKind => Configuration?.Kind;
        public string CarriesSummary => SelectedKind is string k ? L["vehicle.Carries" + k] : L["vehcap.NotConfigured"];
        public bool ShowNotConfigured => HasCards && SelectedKind is null;

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        public VehicleEditViewModel(Vehicle vehicle,
                                    ObservableCollection<VehicleGroup> groups,
                                    ObservableCollection<CapacityDetailType> capacityTypes,
                                    ObservableCollection<VehicleType> vehicleTypes)
        {
            Vehicle = vehicle;
            AllGroups = groups;
            AllCapacityTypes = capacityTypes;
            AllVehicleTypes = vehicleTypes;
            Title = vehicle.Id == 0 ? L["AddNewVehicle"] : L["EditVehicle"];

            // The configuration most vehicles of the kind already have is the one the card gives.
            if (VehicleCapabilityStore.Instance.Current is { } snapshot)
            {
                _configuration = snapshot.Configurations.FirstOrDefault(c => c.Id == vehicle.CapacityDetailTypeId);
                for (var level = 1; level <= 3; level++)
                    Cards.Add(new VehicleKindCard(this, level,
                        snapshot.Configurations.Where(c => c.Level == level).OrderByDescending(c => c.Vehicles).ThenBy(c => c.Name).ToList()));
            }

            SaveCommand = new RelayCommandObject(o => OnSave?.Invoke(), o => CanSave());
            CancelCommand = new RelayCommandObject(o => OnCancel?.Invoke());
        }

        public event System.Action? OnSave;
        public event System.Action? OnCancel;

        private void RaiseChoice()
        {
            foreach (var n in new[] { nameof(Configurations), nameof(ShowConfigurations), nameof(Configuration), nameof(SelectedKind), nameof(CarriesSummary), nameof(ShowNotConfigured) })
                OnPropertyChanged(n);
            foreach (var c in Cards) c.Raise();
        }

        private bool CanSave() =>
            !string.IsNullOrWhiteSpace(Vehicle.Name) && Vehicle.GroupId > 0 && Vehicle.CapacityDetailTypeId > 0 && Vehicle.VehicleTypeId > 0;
    }
}
