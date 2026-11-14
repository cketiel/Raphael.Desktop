using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Compliance;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// The document a compliance notice is about, as it is now (RE-069), for the notice's reading view.
    /// </summary>
    /// <remarks>
    /// Read when the notice is opened, never while the inbox is scrolled: the notice says what happened
    /// then, this card says where the document stands now (approved since, rejected, replaced). The only
    /// way to act on it is Compliance; here it can only be looked at.
    /// </remarks>
    public partial class ComplianceNoticeViewModel : ObservableObject
    {
        private static string T(string key) => LocalizationService.Instance[key];

        private readonly ComplianceService _service = new();
        private NotificationItemViewModel _item;
        private int _version;

        [ObservableProperty] private bool _isVisible;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _error;
        [ObservableProperty] private ComplianceDocumentDto _document;

        /// <summary>The notice points at a version that has since been replaced by a newer one.</summary>
        [ObservableProperty] private bool _isReplaced;

        public bool HasDocument => Document is not null;
        public bool HasError => !string.IsNullOrEmpty(Error);
        public bool CanView => Document?.Files.Count > 0;
        public bool CanOpen => _item?.CanOpenInDocs == true;

        public string DocumentName => Document?.DocumentTypeName;
        public string OwnerText => Document is null ? null
            : $"{Document.SubjectName} · {T(Document.Subject == "VEHICLE" ? "compliance.KindVehicle" : "compliance.KindPerson")}";

        /// <summary>valid, soon, expired, pending, rejected or other: the colour of the badge.</summary>
        public string State => Document?.Status switch
        {
            "PENDING" => "pending",
            "REJECTED" => "rejected",
            "APPROVED" => Document.DaysToExpiry < 0 ? "expired" : Document.DaysToExpiry <= 30 ? "soon" : "valid",
            _ => "other"
        };

        public string StateText => State switch
        {
            "pending" => T("compliance.InReview"),
            "rejected" => T("compliance.Rejected"),
            "expired" => T("compliance.Expired"),
            "soon" => T("compliance.Expiring"),
            "valid" => T("compliance.Valid"),
            _ => Document?.Status
        };

        public string ExpiresText => Document?.ExpiresOn.ToString("MM/dd/yyyy");
        public string UploadedText => Document is null ? null
            : $"{Document.UploadedAtUtc.ToLocalTime():MM/dd/yyyy h:mm tt} · {Document.UploadedByName}";
        public string ReviewedText => Document?.ReviewedAtUtc is DateTime at
            ? $"{at.ToLocalTime():MM/dd/yyyy h:mm tt} · {Document.ReviewedByName}" : "—";
        public string RejectionText => Document?.Status == "REJECTED" ? Document.RejectionReason : null;
        public bool HasRejection => !string.IsNullOrWhiteSpace(RejectionText);

        /// <summary>Shows the card for a compliance notice, or hides it for any other.</summary>
        public async Task ShowAsync(NotificationItemViewModel item)
        {
            var version = ++_version;
            _item = item;
            Document = null;
            IsReplaced = false;
            Error = null;
            IsVisible = item?.IsCompliance == true;
            Raise();
            if (!IsVisible || !item.CanOpenInDocs) return;

            IsLoading = true;
            try
            {
                var subject = await _service.GetSubjectAsync(item.ComplianceSubject, item.ComplianceSubjectId!.Value);
                if (version != _version) return;

                var docs = subject.Items.SelectMany(i => new[] { i.Current, i.Latest }).Where(d => d is not null).ToList();
                var exact = item.ComplianceDocumentId is int id ? docs.FirstOrDefault(d => d.Id == id) : null;
                Document = exact;
                // A notice older than the document's current version: the card says so, and
                // Compliance shows that person's or vehicle's documents as they are now.
                IsReplaced = exact is null && item.ComplianceDocumentId is not null;
            }
            catch (Exception ex)
            {
                if (version == _version) Error = ex.Message;
            }
            finally
            {
                if (version == _version) { IsLoading = false; Raise(); }
            }
        }

        [RelayCommand]
        private async Task ViewAsync()
        {
            if (!CanView) return;
            try { await ComplianceFileViewer.ShowAsync(_service, Document); }
            catch (Exception ex) { Error = ex.Message; Raise(); }
        }

        [RelayCommand]
        private void Open()
        {
            if (!CanOpen) return;
            ComplianceUi.Open(_item.ComplianceSubject, _item.ComplianceSubjectId!.Value, _item.ComplianceDocumentId);
        }

        private void Raise()
        {
            foreach (var name in new[] { nameof(HasDocument), nameof(HasError), nameof(CanView), nameof(CanOpen), nameof(DocumentName),
                         nameof(OwnerText), nameof(State), nameof(StateText), nameof(ExpiresText), nameof(UploadedText),
                         nameof(ReviewedText), nameof(RejectionText), nameof(HasRejection) })
                OnPropertyChanged(name);
        }
    }
}
