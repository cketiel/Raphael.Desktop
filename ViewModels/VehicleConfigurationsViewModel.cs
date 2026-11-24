using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Data.Scheduling.Vehicles;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>A level a class can have, for the combo.</summary>
    public record LevelOption(int Level, string Name);

    /// <summary>A class of seat being edited. Nothing is sent until its row is saved.</summary>
    public partial class CapacityClassRow : ObservableObject
    {
        public CapacityClassRow(CapacityClassDto dto)
        {
            Dto = dto;
            _level = dto.Level;
            _travelsAlone = dto.TravelsAlone;
        }

        public CapacityClassDto Dto { get; }
        public string Name => Dto.Name;
        public string? Description => Dto.Description;
        public bool CanEdit => Dto.CanEdit;

        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty), nameof(Kind), nameof(KindText))] private int _level;
        [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDirty))] private bool _travelsAlone;

        public string? Kind => VehicleCapabilityUi.Kind(Level);
        public string KindText => VehicleCapabilityUi.KindText(Level);
        public bool IsDirty => Level != Dto.Level || TravelsAlone != Dto.TravelsAlone;
    }

    /// <summary>
    /// RE-083, administrators: the vehicle configurations (what fits in each) and the classes of seat
    /// (what vehicle each needs and whether its patient travels alone).
    /// </summary>
    public partial class VehicleConfigurationsViewModel : ObservableObject
    {
        private readonly IVehicleCapabilityService _service = new VehicleCapabilityService();
        private static string T(string key) => LocalizationService.Instance[key];

        public LocalizationService L => LocalizationService.Instance;
        public ObservableCollection<VehicleConfigurationDto> Configurations { get; } = new();
        public ObservableCollection<CapacityClassRow> Classes { get; } = new();

        public List<LevelOption> Levels { get; } = new() { new(1, "AMB"), new(2, "WCH"), new(3, "STR") };

        [ObservableProperty] private VehicleConfigurationDto? _selected;

        public VehicleConfigurationsViewModel()
        {
            WeakEventManager<VehicleCapabilityStore, EventArgs>.AddHandler(VehicleCapabilityStore.Instance, nameof(VehicleCapabilityStore.Changed), OnStoreChanged);
            Fill();
            _ = VehicleCapabilityStore.Instance.RefreshAsync();
        }

        private void OnStoreChanged(object? sender, EventArgs e) => Fill();

        private void Fill()
        {
            if (VehicleCapabilityStore.Instance.Current is not { } snapshot) return;
            Configurations.Clear();
            foreach (var c in snapshot.Configurations.OrderBy(c => c.Level).ThenBy(c => c.Name)) Configurations.Add(c);

            // A class somebody is editing keeps what they typed: another screen's refresh must not undo it.
            var editing = Classes.Where(c => c.IsDirty).ToDictionary(c => c.Dto.Id);
            Classes.Clear();
            foreach (var c in snapshot.Classes)
            {
                var row = new CapacityClassRow(c);
                if (editing.TryGetValue(c.Id, out var was)) { row.Level = was.Level; row.TravelsAlone = was.TravelsAlone; }
                Classes.Add(row);
            }
        }

        [RelayCommand]
        private async Task RefreshAsync() => await VehicleCapabilityStore.Instance.RefreshAsync();

        [RelayCommand]
        private void Add() => Open(null);

        [RelayCommand]
        private void Edit(VehicleConfigurationDto? c) { if ((c ?? Selected) is { } target) Open(target); }

        private static void Open(VehicleConfigurationDto? configuration)
        {
            var vm = new VehicleConfigurationEditViewModel(configuration);
            var view = new VehicleConfigurationEditView { DataContext = vm, Owner = Application.Current.MainWindow };
            vm.Saved += () => view.DialogResult = true;
            view.ShowDialog();
        }

        [RelayCommand]
        private async Task SaveClassAsync(CapacityClassRow? row)
        {
            if (row is not { IsDirty: true, CanEdit: true }) return;

            // It changes what every vehicle with that seat can carry: said before, not discovered after.
            if (MessageBox.Show(string.Format(T("vehcfg.ConfirmClass"), row.Name, VehicleCapabilityUi.Kind(row.Level)), T("vehcfg.Classes"),
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await _service.UpdateClassAsync(row.Dto.Id, new CapacityClassUpdateDto { Level = row.Level, TravelsAlone = row.TravelsAlone });
                row.Dto.Level = row.Level;
                row.Dto.TravelsAlone = row.TravelsAlone;
                await VehicleCapabilityStore.Instance.RefreshAsync();
            }
            catch (Exception ex)
            {
                UiError.Show(ex.Message, T("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>One line of a configuration being edited.</summary>
    public partial class SeatRow : ObservableObject
    {
        public SeatRow(Action changed) => _changed = changed;
        private readonly Action _changed;

        [ObservableProperty] private SpaceTypeLevelDto? _spaceType;
        [ObservableProperty] private int _quantity = 1;

        partial void OnSpaceTypeChanged(SpaceTypeLevelDto? value) => _changed();
        partial void OnQuantityChanged(int value)
        {
            if (value < 0) { Quantity = 0; return; }
            if (value > 20) { Quantity = 20; return; }
            _changed();
        }

        [RelayCommand] private void More() => Quantity++;
        [RelayCommand] private void Less() => Quantity--;
    }

    public partial class VehicleConfigurationEditViewModel : ObservableObject
    {
        private readonly IVehicleCapabilityService _service = new VehicleCapabilityService();
        private readonly VehicleConfigurationDto? _original;
        private static string T(string key) => LocalizationService.Instance[key];

        public LocalizationService L => LocalizationService.Instance;
        public event Action? Saved;

        public VehicleConfigurationEditViewModel(VehicleConfigurationDto? original)
        {
            _original = original;
            SpaceTypes = VehicleCapabilityStore.Instance.Current?.SpaceTypes.OrderBy(s => s.Level).ThenBy(s => s.Name).ToList() ?? new();
            _name = original?.Name ?? "";
            _description = original?.Description;
            foreach (var s in original?.Seats ?? new())
                Seats.Add(new SeatRow(Changed) { SpaceType = SpaceTypes.FirstOrDefault(t => t.Id == s.SpaceTypeId), Quantity = s.Quantity });
            if (Seats.Count == 0) Add();
        }

        public List<SpaceTypeLevelDto> SpaceTypes { get; }
        public ObservableCollection<SeatRow> Seats { get; } = new();

        public string Title => T(_original is null ? "vehcfg.New" : "vehcfg.Edit");
        public bool IsReadOnly => _original is { CanEdit: false };
        public bool CanEdit => !IsReadOnly;

        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand))] private string _name;
        [ObservableProperty] private string? _description;
        [ObservableProperty] private string? _error;

        private int Level => Seats.Where(s => s.Quantity > 0 && s.SpaceType != null).Select(s => s.SpaceType!.Level).DefaultIfEmpty(0).Max();
        public string? Kind => VehicleCapabilityUi.Kind(Level);
        public string KindText => VehicleCapabilityUi.KindText(Level);
        public string Carries => Kind is string k ? T("vehicle.Carries" + k) : T("vehcap.NoSeats");
        public string UsedBy => _original is { Vehicles: > 0 } ? string.Format(T("vehcfg.UsedBy"), _original.Vehicles) : "";

        private void Changed()
        {
            foreach (var n in new[] { nameof(Kind), nameof(KindText), nameof(Carries) }) OnPropertyChanged(n);
            SaveCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void Add()
        {
            // The next seat nobody has listed: one row per space type.
            var used = Seats.Select(s => s.SpaceType?.Id).ToHashSet();
            var next = new[] { "AMB", "WCH", "STR" }.Select(n => SpaceTypes.FirstOrDefault(t => t.Name == n && !used.Contains(t.Id))).FirstOrDefault(t => t != null);
            Seats.Add(new SeatRow(Changed) { SpaceType = next });
            Changed();
        }

        [RelayCommand]
        private void Remove(SeatRow? row)
        {
            if (row is null) return;
            Seats.Remove(row);
            Changed();
        }

        private bool CanSave() => CanEdit && !string.IsNullOrWhiteSpace(Name);

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            Error = null;
            if (Seats.Any(s => s.SpaceType is null)) { Error = T("vehcfg.MissingSpaceType"); return; }
            if (Seats.GroupBy(s => s.SpaceType!.Id).Any(g => g.Count() > 1)) { Error = T("vehcfg.Duplicate"); return; }

            // Fewer or other seats change what every vehicle with it may be routed.
            if (_original is { Vehicles: > 0 } && _original.Level != Level &&
                MessageBox.Show(string.Format(T("vehcfg.ConfirmKind"), _original.Vehicles, _original.KindText, KindText), Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await _service.SaveConfigurationAsync(_original?.Id, new VehicleConfigurationSaveDto
                {
                    Name = Name.Trim(),
                    Description = Description,
                    Seats = Seats.Select(s => new VehicleConfigurationSeatSaveDto { SpaceTypeId = s.SpaceType!.Id, Quantity = s.Quantity }).ToList()
                });
                await VehicleCapabilityStore.Instance.RefreshAsync();
                Saved?.Invoke();
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }
    }
}
