using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Data.Scheduling.Vehicles;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows; // Para MessageBox, aunque es mejor usar un servicio de diálogo

namespace Raphael.Desktop.ViewModels
{
    // Heredamos de ObservableObject y marcamos como partial
    public partial class SpaceTypesViewModel : ObservableObject
    {
        #region Services
        private readonly SpaceTypeService _spaceTypeService;
        #endregion

        #region Translation
        public string SpaceTypeNameText => LocalizationService.Instance["SpaceTypeName"];
        public string DescriptionText => LocalizationService.Instance["Description"];
        public string LoadTimeText => LocalizationService.Instance["LoadTime"];
        public string UnloadTimeText => LocalizationService.Instance["UnloadTime"];
        public string CapacityTypeText => LocalizationService.Instance["CapacityType"];
        public string InactiveText => LocalizationService.Instance["Inactive"];
        public string AddSpaceTypeToolTip => LocalizationService.Instance["AddSpaceType"];
        public string DeleteSpaceTypeToolTip => LocalizationService.Instance["DeleteSpaceType"];
        #endregion

        // Propiedad ObservableCollection (no necesita [ObservableProperty] directamente)
        public ObservableCollection<SpaceType> SpaceTypes { get; set; } = new();

        // Propiedad observable para el SpaceType seleccionado
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(DeleteSpaceTypeCommand))] // Notifica a DeleteSpaceTypeCommand cuando cambia
        private SpaceType _selectedSpaceType;

        // ---- RE-083: the vehicle each type needs, search, and changing its class ----

        public LocalizationService L => LocalizationService.Instance;
        public System.ComponentModel.ICollectionView SpaceTypesListView { get; }
        public ObservableCollection<DTOs.CapacityClassDto> Classes { get; } = new();

        private string _search = string.Empty;
        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? string.Empty)) SpaceTypesListView.Refresh(); }
        }

        private bool Matches(object o)
        {
            var q = Search.Trim();
            return q.Length == 0 || o is SpaceType s &&
                (s.Name?.Contains(q, System.StringComparison.OrdinalIgnoreCase) == true || s.Description?.Contains(q, System.StringComparison.OrdinalIgnoreCase) == true);
        }

        private int _warnings;
        public bool HasWarnings => _warnings > 0;
        public string WarningsText => string.Format(L["spacetypes.Warnings"], _warnings);

        /// <summary>
        /// Puts the class and level on each row. A type whose own name is a class but which hangs
        /// from another one (an import that fell back to AMB) is pointed out, not changed.
        /// </summary>
        private void MarkClasses()
        {
            if (VehicleCapabilityStore.Instance.Current is not { } snapshot) return;
            if (Classes.Count != snapshot.Classes.Count)
            {
                Classes.Clear();
                foreach (var c in snapshot.Classes) Classes.Add(c);
            }
            foreach (var s in SpaceTypes)
            {
                var type = snapshot.SpaceTypes.Find(t => t.Id == s.Id);
                if (type is null) continue;
                s.Level = type.Level;
                s.ClassId = type.CapacityTypeId;
                s.CanEditClass = type.CanEdit && Helpers.TripGuard.IsAdmin;
                var own = snapshot.Classes.Find(c => string.Equals(c.Name, s.Name, System.StringComparison.OrdinalIgnoreCase));
                s.ClassWarning = type.Level <= 0 ? L["spacetypes.WarnNoClass"]
                    : own is not null && own.Id != type.CapacityTypeId ? string.Format(L["spacetypes.WarnOtherClass"], s.Name, type.CapacityType)
                    : null;
            }
            _warnings = System.Linq.Enumerable.Count(SpaceTypes, s => s.HasClassWarning);
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(WarningsText));
            SpaceTypesListView.Refresh();
        }

        /// <summary>Moves a type to another class, after saying what it changes.</summary>
        public async Task ChangeClassAsync(SpaceType type, DTOs.CapacityClassDto to)
        {
            if (to.Id != type.ClassId && type.CanEditClass &&
                MessageBox.Show(string.Format(L["spacetypes.ConfirmClass"], type.Name, to.Name, Helpers.VehicleCapabilityUi.Kind(to.Level)), L["SpaceTypes"],
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    await new VehicleCapabilityService().UpdateSpaceTypeClassAsync(type.Id, to.Id);
                    await VehicleCapabilityStore.Instance.RefreshAsync();
                }
                catch (System.Exception ex)
                {
                    Helpers.UiError.Show(ex.Message, L["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            // Also when it was refused: the combo goes back to what the type has.
            MarkClasses();
        }

        /// <summary>RE-093: new minutes to board or to leave. Routes planned from now on use them.</summary>
        public async Task ChangeTimeAsync(SpaceType type, bool load, string text)
        {
            var was = load ? type.LoadTime : type.UnloadTime;
            if (!type.CanEditClass || !float.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
                || minutes < 0 || minutes > 120 || System.Math.Abs(minutes - was) < 0.05f) return;

            if (MessageBox.Show(string.Format(L[load ? "spacetypes.ConfirmLoad" : "spacetypes.ConfirmUnload"], type.Name, was, minutes), L["SpaceTypes"],
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await new VehicleCapabilityService().UpdateSpaceTypeTimesAsync(type.Id, load ? minutes : type.LoadTime, load ? type.UnloadTime : minutes);
                if (load) type.LoadTime = minutes; else type.UnloadTime = minutes;
                await VehicleCapabilityStore.Instance.RefreshAsync();
            }
            catch (System.Exception ex)
            {
                Helpers.UiError.Show(ex.Message, L["ErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCapabilityChanged(object? sender, System.EventArgs e) => MarkClasses();

        public SpaceTypesViewModel()
        {
            _spaceTypeService = new SpaceTypeService();
            SpaceTypesListView = System.Windows.Data.CollectionViewSource.GetDefaultView(SpaceTypes);
            SpaceTypesListView.Filter = Matches;
            WeakEventManager<VehicleCapabilityStore, System.EventArgs>.AddHandler(VehicleCapabilityStore.Instance, nameof(VehicleCapabilityStore.Changed), OnCapabilityChanged);
            _ = LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            await Task.WhenAll(LoadSpaceTypesAsync(), VehicleCapabilityStore.Instance.RefreshAsync());
            MarkClasses();
        }

        public async Task LoadSpaceTypesAsync()
        {
            try
            {
                var sources = await _spaceTypeService.GetSpaceTypesAsync();
                SpaceTypes.Clear();
                foreach (var source in sources)
                {
                    SpaceTypes.Add(source);
                }
            }
            catch (ApiException ex)
            {
                Helpers.UiError.Show($"Error loading data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Comando asíncrono para añadir
        [RelayCommand] // No necesita CanExecute, ya que siempre se puede añadir
        private async Task AddSpaceTypeAsync()
        {
            var addVM = new AddSpaceTypeViewModel();
            var addView = new AddSpaceTypeView(addVM);

            if (addView.ShowDialog() == true)
            {
                try
                {
                    var createdSpaceType = await _spaceTypeService.CreateSpaceTypeAsync(addVM.NewSpaceType);
                    SpaceTypes.Add(createdSpaceType);
                    await VehicleCapabilityStore.Instance.RefreshAsync();
                }
                catch (ApiException ex)
                {
                    Helpers.UiError.Show($"Error saving the new Space Type: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Comando asíncrono para eliminar, con condición CanExecute
        [RelayCommand(CanExecute = nameof(CanDeleteSpaceType))]
        private async Task DeleteSpaceTypeAsync(SpaceType? row)
        {
            // The row whose icon was pressed: the selection could be another one.
            if (row != null) SelectedSpaceType = row;
            // La condición CanExecute ya previene que esto se llame si SelectedSpaceType es null,
            // pero una doble verificación no hace daño.
            if (SelectedSpaceType == null) return;

            var result = MessageBox.Show($"Are you sure you want to delete '{SelectedSpaceType.Name}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _spaceTypeService.DeleteSpaceTypeAsync(SelectedSpaceType.Id);
                    SpaceTypes.Remove(SelectedSpaceType);
                }
                catch (ApiException ex)
                {
                    Helpers.UiError.Show($"Error deleting Space Type: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Método que determina si el comando Delete puede ejecutarse
        private bool CanDeleteSpaceType(SpaceType? row)
        {
            return row != null || SelectedSpaceType != null;
        }
    }
}