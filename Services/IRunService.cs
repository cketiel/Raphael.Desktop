using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IRunService
    {
        Task<List<VehicleRoute>> GetAllAsync();
        Task<VehicleRoute> GetByIdAsync(int id);
        Task<VehicleRoute> CreateAsync(VehicleRouteDto dto);
        /// <summary>
        /// Saves the route. When its garage changed, answers with what that did to the planned days
        /// (RE-052); null otherwise.
        /// </summary>
        Task<GarageChangePreviewDto> UpdateAsync(int id, VehicleRouteDto dto,
            string confirmedFingerprint = null, System.Threading.CancellationToken ct = default);

        /// <summary>What a new garage would do to the days already planned on this route. Saves nothing.</summary>
        Task<GarageChangePreviewDto> PreviewGarageChangeAsync(int id, GarageChangeRequestDto request,
            System.Threading.CancellationToken ct = default);
        Task DeleteAsync(int id);
        Task<bool> CancelAsync(int id);

        /// <summary>
        /// The routes in service from today on that the route described would clash with (RE-063): the
        /// driver's route and the route using the login. Saves nothing.
        /// </summary>
        Task<List<RunConflictDto>> FindConflictsAsync(RunConflictQueryDto query);

        /// <summary>Ends a route on a day, its last in service (RE-063). Throws ApiException with the server's reason when refused.</summary>
        Task<VehicleRoute> EndAsync(int id, DateTime endDate);
    }
}
