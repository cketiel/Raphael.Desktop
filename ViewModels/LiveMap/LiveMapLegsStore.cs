using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>
    /// The legs of each line's day (RE-080): read from the API, shapes filled in, kept per line.
    /// </summary>
    /// <remarks>
    /// <para>The API returns the shapes the routing cache already holds. A leg nobody has drawn yet
    /// is asked of the routing endpoint, cache-first: it is bought once, at the hour the route plans
    /// to drive it, and from then on everybody reads it from the cache (MAPS_POLICY). Only the legs
    /// that will be drawn are asked for: the current one of every line, and the whole day of the line
    /// whose card is open.</para>
    /// <para>If a shape cannot be had (no answer, no key), the leg is drawn as a straight line between
    /// its two points: the map says where the vehicle goes, if not by which street.</para>
    /// </remarks>
    public class LiveMapLegsStore
    {
        /// <summary>A line's legs are refreshed on a live fix at most this often.</summary>
        private static readonly TimeSpan FixRefresh = TimeSpan.FromMinutes(2);

        private readonly ILiveMapService _service;
        private readonly IRoutingApiService _routing;
        private readonly Dictionary<int, LiveMapRouteLegsDto> _byRoute = new();
        private readonly Dictionary<int, DateTime> _loadedAt = new();
        private readonly Dictionary<string, string> _shapes = new();
        private readonly HashSet<string> _noShape = new();
        private readonly HashSet<int> _loading = new();

        public LiveMapLegsStore(ILiveMapService service, IRoutingApiService routing)
        {
            _service = service;
            _routing = routing;
        }

        /// <summary>Raised on the UI thread when a line's legs change.</summary>
        public event Action<int> Changed;

        /// <summary>The line whose whole day is drawn (its card is open), or null.</summary>
        public int? Selected { get; private set; }

        public LiveMapRouteLegsDto For(int vehicleRouteId) =>
            _byRoute.TryGetValue(vehicleRouteId, out var legs) ? legs : null;

        public async Task SelectAsync(int? vehicleRouteId)
        {
            var previous = Selected;
            Selected = vehicleRouteId;
            if (previous is int p && p != vehicleRouteId) Changed?.Invoke(p);
            if (vehicleRouteId is int id) await LoadAsync(id, force: true);
        }

        /// <summary>Reads (or re-reads) a line's legs and fills the shapes it is going to draw.</summary>
        public async Task LoadAsync(int vehicleRouteId, bool force = false)
        {
            if (!force && _loadedAt.TryGetValue(vehicleRouteId, out var at) && DateTime.UtcNow - at < FixRefresh) return;
            if (!_loading.Add(vehicleRouteId)) return;

            try
            {
                var legs = await _service.GetRouteLegsAsync(vehicleRouteId);
                _loadedAt[vehicleRouteId] = DateTime.UtcNow;
                if (legs is null) { _byRoute.Remove(vehicleRouteId); Changed?.Invoke(vehicleRouteId); return; }

                _byRoute[vehicleRouteId] = legs;
                Changed?.Invoke(vehicleRouteId);   // draw now with what the cache had

                var wanted = legs.Legs.Where(l => l.Visible && (l.Current || Selected == vehicleRouteId)).ToList();
                var filled = false;
                foreach (var leg in wanted)
                    filled |= await FillShapeAsync(leg, legs.OperatingDate);

                if (filled) Changed?.Invoke(vehicleRouteId);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: could not read the legs of route {vehicleRouteId}. {ex.Message}");
            }
            finally
            {
                _loading.Remove(vehicleRouteId);
            }
        }

        /// <summary>Forgets a line's legs, so the next load reads them again (it was re-planned).</summary>
        public void Invalidate(int vehicleRouteId) => _loadedAt.Remove(vehicleRouteId);

        private async Task<bool> FillShapeAsync(LiveMapLegDto leg, DateTime date)
        {
            if (!string.IsNullOrEmpty(leg.EncodedPolyline)) return false;

            var key = $"{leg.FromLatitude:F5},{leg.FromLongitude:F5}>{leg.ToLatitude:F5},{leg.ToLongitude:F5}";
            if (_shapes.TryGetValue(key, out var known)) { leg.EncodedPolyline = known; return true; }
            if (_noShape.Contains(key)) return false;

            try
            {
                var result = await _routing.GetMapRouteAsync(leg.FromLatitude, leg.FromLongitude, leg.ToLatitude, leg.ToLongitude, date, leg.Departure);
                if (result is { IsUsable: true } && !string.IsNullOrEmpty(result.EncodedPolyline))
                {
                    _shapes[key] = leg.EncodedPolyline = result.EncodedPolyline;
                    return true;
                }
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Live map: no shape for a leg. {ex.Message}");
            }

            _noShape.Add(key);   // straight line from now on; not asked again this session
            return false;
        }
    }
}
