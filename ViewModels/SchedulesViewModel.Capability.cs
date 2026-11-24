using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// RE-083: says when the line on screen cannot carry the trip about to be routed, and keeps
    /// saying it after it was routed anyway. A warning and nothing else: it never blocks, never asks
    /// and never calls the server — the answer is read from memory.
    /// </summary>
    public partial class SchedulesViewModel
    {
        private string? _capabilityNotice;

        /// <summary>The trip selected now; when that one is fine, the last one routed against the rule.</summary>
        public string? CapabilityText => SelectedCapabilityWarning ?? _capabilityNotice;
        public bool HasCapabilityText => CapabilityText != null;
        public bool CanDismissCapability => _capabilityNotice != null && SelectedCapabilityWarning is null;
        public string CapabilityDismissText => LocalizationService.Instance["vehcap.Dismiss"];

        private string? SelectedCapabilityWarning
        {
            get
            {
                var why = CapabilityWarningFor(SelectedVehicleRoute, SelectedUnscheduledTrip);
                return why is null ? null : $"{why} {LocalizationService.Instance["vehcap.StillRoute"]}";
            }
        }

        private static string? CapabilityWarningFor(VehicleRoute? route, UnscheduledTripDto? trip) =>
            route is null || trip is null ? null : VehicleCapabilityStore.Instance.Warning(route.Name, route.VehicleId, trip.SpaceType);

        private IRelayCommand? _dismissCapabilityCommand;
        public IRelayCommand DismissCapabilityCommand => _dismissCapabilityCommand ??= new RelayCommand(() => { _capabilityNotice = null; RaiseCapability(); });

        /// <summary>Reads the rules in the background; the tab never waits for them.</summary>
        private void StartCapability()
        {
            WeakEventManager<VehicleCapabilityStore, EventArgs>.AddHandler(VehicleCapabilityStore.Instance, nameof(VehicleCapabilityStore.Changed), OnCapabilityRulesChanged);
            _ = VehicleCapabilityStore.Instance.EnsureAsync();
        }

        private void OnCapabilityRulesChanged(object? sender, EventArgs e) => OnUiThread(RaiseCapability);

        /// <summary>After a routing that succeeded: what was routed against the rule stays in sight.</summary>
        private void NoteCapabilityAfterRouting(VehicleRoute route, UnscheduledTripDto trip)
        {
            var why = CapabilityWarningFor(route, trip);
            _capabilityNotice = why is null ? null : string.Format(LocalizationService.Instance["vehcap.Routed"], trip.TripId, why);
            RaiseCapability();
        }

        private void RaiseCapability()
        {
            OnPropertyChanged(nameof(CapabilityText));
            OnPropertyChanged(nameof(HasCapabilityText));
            OnPropertyChanged(nameof(CanDismissCapability));
        }
    }
}
