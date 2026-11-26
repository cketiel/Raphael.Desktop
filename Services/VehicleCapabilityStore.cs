using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// RE-083: what each vehicle can carry, read once and shared by every screen, so the warning at
    /// routing costs no call. Read again after a change made here, or when it is older than
    /// <see cref="MaxAge"/> and a screen asks; never on a timer and never while routing waits.
    /// </summary>
    public sealed class VehicleCapabilityStore
    {
        public static VehicleCapabilityStore Instance { get; } = new();
        private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);

        private readonly IVehicleCapabilityService _service = new VehicleCapabilityService();
        private Task<VehicleCapabilityDto?>? _loading;
        private DateTime _loadedUtc = DateTime.MinValue;
        private Dictionary<int, int> _vehicleLevels = new();
        private Dictionary<string, SpaceTypeLevelDto> _spaceTypes = new(StringComparer.OrdinalIgnoreCase);

        private VehicleCapabilityStore() { }

        /// <summary>Null until read, or when the server does not have it: then nobody is warned of anything.</summary>
        public VehicleCapabilityDto? Current { get; private set; }

        public event EventHandler? Changed;

        /// <summary>The snapshot, read only if there is none or it is old. Never throws.</summary>
        public Task<VehicleCapabilityDto?> EnsureAsync() =>
            DateTime.UtcNow - _loadedUtc < MaxAge ? Task.FromResult(Current) : RefreshAsync();

        public Task<VehicleCapabilityDto?> RefreshAsync() =>
            _loading is { IsCompleted: false } ? _loading : _loading = LoadAsync();

        private async Task<VehicleCapabilityDto?> LoadAsync()
        {
            try
            {
                var snapshot = await _service.GetAsync();
                _loadedUtc = DateTime.UtcNow;
                if (snapshot is null) return Current;

                var levels = new Dictionary<int, int>();
                foreach (var v in snapshot.Vehicles) levels[v.VehicleId] = v.Level;
                var types = new Dictionary<string, SpaceTypeLevelDto>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in snapshot.SpaceTypes) types[s.Name] = s;

                _vehicleLevels = levels;
                _spaceTypes = types;
                Current = snapshot;
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Helpers.FileLogger.Log($"Vehicle capability: could not be read. {ex.Message}");
            }
            return Current;
        }

        public int LevelOfVehicle(int vehicleId) => _vehicleLevels.GetValueOrDefault(vehicleId);

        /// <summary>RE-093: minutes to board a trip of this space type; fifteen when it declares none.</summary>
        public int BoardingMinutes(string? spaceType)
        {
            var load = spaceType is not null && _spaceTypes.TryGetValue(spaceType.Trim(), out var type) ? type.LoadTime : 0;
            return load >= 0.5f ? (int)Math.Round(load, MidpointRounding.AwayFromZero) : 15;
        }

        /// <summary>Why this vehicle cannot carry this space type, or null. In memory, no call.</summary>
        public string? Warning(string? route, int vehicleId, string? spaceType)
        {
            if (Current is null || string.IsNullOrWhiteSpace(spaceType)) return null;
            // A vehicle or a space type newer than the snapshot cannot be judged: say nothing.
            if (!_vehicleLevels.TryGetValue(vehicleId, out var level)) return null;
            if (!_spaceTypes.TryGetValue(spaceType.Trim(), out var type)) return null;
            return Helpers.VehicleCapabilityUi.Explain(route, level, type.Name, type.Level);
        }
    }
}
