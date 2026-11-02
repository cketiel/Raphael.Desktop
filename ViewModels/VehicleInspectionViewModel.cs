using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>A line of the inspection as the office reads it, in the office's language.</summary>
    public class InspectionLineView
    {
        public string Code { get; init; }
        public string Text { get; init; }
        public bool IsProblem { get; init; }
        public bool HasPhoto { get; init; }
        public string ResultText { get; init; }
    }

    /// <summary>
    /// The vehicle inspections of one route-day, Pull-out and Pull-in (RE-062), with the photos of
    /// the problems and the driver's signature, fetched only when the dispatcher asks for them.
    /// </summary>
    public partial class VehicleInspectionViewModel : ObservableObject
    {
        private readonly VehicleInspectionService _service = new();
        private static string T(string key) => LocalizationService.Instance[key];

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _message;
        [ObservableProperty] private VehicleInspectionDetailDto _selected;
        [ObservableProperty] private BitmapImage _photo;
        [ObservableProperty] private string _photoCaption;
        [ObservableProperty] private BitmapImage _signature;

        public ObservableCollection<VehicleInspectionDetailDto> Inspections { get; } = new();
        public ObservableCollection<InspectionLineView> Lines { get; } = new();

        public string TitleText => T("inspection.Title");
        public string OdometerText => T("inspection.Odometer");
        public string VinText => T("inspection.Vin");
        public string CommentText => T("inspection.Comment");
        public string SignatureText => T("inspection.Signature");
        public string ViewPhotoText => T("inspection.ViewPhoto");
        public string CloseText => T("inspection.Close");

        public string KindText(VehicleInspectionDetailDto i) => i?.Kind == "PullIn" ? "Pull-in" : "Pull-out";

        public string Header => Selected is null ? string.Empty :
            $"{KindText(Selected)} · {Selected.VehicleName} · {Selected.DriverName} · {Selected.OperatingDate:MM/dd/yyyy}";

        public string TimesText => Selected is null ? string.Empty :
            string.Format(T("inspection.Times"), Selected.StartedAtUtc.ToLocalTime().ToString("t"), Selected.CompletedAtUtc.ToLocalTime().ToString("t"),
                Selected.ReceivedAtUtc - Selected.CompletedAtUtc > TimeSpan.FromMinutes(5)
                    ? " " + string.Format(T("inspection.ReceivedLate"), Selected.ReceivedAtUtc.ToLocalTime().ToString("g"))
                    : string.Empty);

        public string VinLine => Selected is null ? string.Empty
            : Selected.VinConfirmed
                ? string.Format(T("inspection.VinConfirmed"), Selected.VinOnFile)
                : string.Format(T("inspection.VinDifferent"), Selected.VinReported ?? "—", Selected.VinOnFile ?? T("inspection.VinNone"));

        public VehicleInspectionViewModel(int vehicleRouteId, DateTime date, int? scheduleId = null)
        {
            _ = LoadAsync(vehicleRouteId, date, scheduleId);
        }

        /// <summary>Opened from a notification: one inspection by its id.</summary>
        public VehicleInspectionViewModel(int inspectionId)
        {
            _ = LoadOneAsync(inspectionId);
        }

        private async Task LoadAsync(int vehicleRouteId, DateTime date, int? scheduleId)
        {
            IsLoading = true;
            try
            {
                var list = await _service.GetForRouteDayAsync(vehicleRouteId, date);
                foreach (var i in list) Inspections.Add(i);
                Selected = list.FirstOrDefault(i => i.ScheduleId == scheduleId) ?? list.FirstOrDefault();
                if (Selected is null) Message = T("inspection.None");
            }
            catch (Exception ex)
            {
                Message = $"{T("inspection.LoadError")} {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadOneAsync(int id)
        {
            IsLoading = true;
            try
            {
                var one = await _service.GetAsync(id);
                if (one is not null) { Inspections.Add(one); Selected = one; }
                else Message = T("inspection.None");
            }
            catch (Exception ex)
            {
                Message = $"{T("inspection.LoadError")} {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSelectedChanged(VehicleInspectionDetailDto value)
        {
            Lines.Clear();
            Photo = null;
            PhotoCaption = null;
            Signature = null;
            OnPropertyChanged(nameof(Header));
            OnPropertyChanged(nameof(TimesText));
            OnPropertyChanged(nameof(VinLine));
            if (value is null) return;

            foreach (var item in value.Items)
            {
                var key = $"inspection.item.{item.Code}";
                var text = T(key);
                Lines.Add(new InspectionLineView
                {
                    Code = item.Code,
                    Text = string.IsNullOrEmpty(text) || text.StartsWith("##") ? item.Label : text,
                    IsProblem = item.IsProblem,
                    HasPhoto = item.HasPhoto,
                    ResultText = item.IsProblem ? T("inspection.Problem") : T("inspection.Ok")
                });
            }

            if (value.HasSignature) _ = LoadSignatureAsync(value.Id);
        }

        private async Task LoadSignatureAsync(int id)
        {
            var bytes = await _service.GetSignatureAsync(id);
            if (Selected?.Id == id) Signature = ToImage(bytes);
        }

        [RelayCommand]
        private async Task ShowPhoto(InspectionLineView line)
        {
            if (Selected is null || line is null) return;
            var bytes = await _service.GetPhotoAsync(Selected.Id, line.Code);
            Photo = ToImage(bytes);
            PhotoCaption = Photo is null ? T("inspection.PhotoMissing") : line.Text;
        }

        [RelayCommand]
        private async Task ShowVinPhoto()
        {
            if (Selected is null) return;
            var bytes = await _service.GetPhotoAsync(Selected.Id, "vin");
            Photo = ToImage(bytes);
            PhotoCaption = Photo is null ? T("inspection.PhotoMissing") : VinText;
        }

        /// <summary>In memory only: the evidence is never written to the dispatcher's disk.</summary>
        private static BitmapImage ToImage(byte[] bytes)
        {
            if (bytes is null || bytes.Length == 0) return null;
            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
