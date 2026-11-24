using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>RE-089: the columns of the trips, as this user likes them.</summary>
    public partial class HomeViewModel
    {
        private const string LegacyColumnsKey = "HomeGridColumns";

        /// <summary>What each column asks before drawing itself.</summary>
        public ColumnVisibilityMap ColumnVisibility { get; } = new();

        public HomeColumnsViewModel Columns { get; private set; }

        /// <summary>RE-050: for Raphael's admin only, who may hide it like any other.</summary>
        public Visibility ProviderColumnVisibility => IsPlatformAdmin && Columns?.IsShown("ProviderName") == true ? Visibility.Visible : Visibility.Collapsed;

        public string CancelledByHeader => LocalizationService.Instance["cancel.ColBy"];
        public string CancellationHeader => LocalizationService.Instance["cancel.ColReason"];

        /// <summary>The default order, and what shows before anybody chooses.</summary>
        private IReadOnlyList<(string Key, string Header, bool Visible)> DefaultColumns()
        {
            var list = new List<(string, string, bool)>
            {
                ("Day", DayText, true), ("Date", DateText, true), ("FromTime", FromTimeText, true), ("ToTime", ToTimeText, true),
                ("CustomerName", CustomerNameText, true), ("RunName", RunText, true)
            };
            if (IsPlatformAdmin) list.Add(("ProviderName", ProviderText, true));
            list.AddRange(new[]
            {
                ("PickupAddress", PickupAddressText, true), ("DropoffAddress", DropoffAddressText, true),
                ("SpaceTypeName", SpaceTypeNameText, true), ("Charge", ChargeText, true), ("Paid", PaidText, true),
                ("PickupComment", PickupCommentText, false), ("DropoffComment", DropoffCommentText, false),
                ("Type", TypeText, true), ("Pickup", PickupText, true), ("Dropoff", DropoffText, true),
                ("PickupPhone", PickupPhoneText, true), ("DropoffPhone", DropoffPhoneText, true), ("TripId", TripIdText, true),
                ("Authorization", AuthorizationText, true), ("FundingSourceName", FundingSourceText, true),
                ("Distance", DistanceText, true), ("PickupCity", PickupCityText, true), ("DropoffCity", DropoffCityText, true),
                ("CancelledByText", CancelledByHeader, true), ("CancellationText", CancellationHeader, true),
                ("DriverNoShowReason", DriverNoShowReasonText, true)
            });
            return list;
        }

        private void InitializeColumns()
        {
            Columns = new HomeColumnsViewModel(DefaultColumns, LegacyColumnsKey);
            Columns.Changed += ApplyColumns;
            ApplyColumns();

            IsCompactGrid = _config.Load<bool>(CompactKey);
        }

        private void ApplyColumns()
        {
            ColumnVisibility.Apply(Columns.Layout);
            OnPropertyChanged(nameof(ProviderColumnVisibility));
        }
    }
}
