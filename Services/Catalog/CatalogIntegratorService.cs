using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// The directory of entities that could send us trips: nursing homes, assisted living
    /// residences, hospitals.
    /// </summary>
    public interface ICatalogIntegratorService
    {
        Task<CatalogPageDto<CatalogIntegratorListItemDto>> SearchAsync(
            CatalogSearchRequestDto request, CancellationToken cancellationToken = default);

        Task<CatalogIntegratorDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<CatalogIntegratorDto> CreateAsync(
            CatalogIntegratorSaveDto request, CancellationToken cancellationToken = default);

        Task<CatalogIntegratorDto> UpdateAsync(
            int id, CatalogIntegratorSaveDto request, CancellationToken cancellationToken = default);

        Task<CatalogImportResultDto> ImportAsync(
            CatalogImportRequestDto<CatalogIntegratorImportRowDto> request,
            CancellationToken cancellationToken = default);

        Task<CatalogGeocodeResultDto> GeocodeAsync(
            System.Collections.Generic.IEnumerable<int> ids,
            CancellationToken cancellationToken = default);

        /// <summary>The integrator form, filled in from the catalog row, for the user to confirm.</summary>
        Task<Integrator?> GetCompanyDraftAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="ICatalogIntegratorService"/>
    public class CatalogIntegratorService
        : CatalogServiceBase<
            CatalogIntegratorListItemDto,
            CatalogIntegratorDto,
            CatalogIntegratorSaveDto,
            CatalogIntegratorImportRowDto>,
          ICatalogIntegratorService
    {
        public CatalogIntegratorService()
            : base("admin/catalog/integrators")
        {
        }

        public Task<Integrator?> GetCompanyDraftAsync(int id, CancellationToken cancellationToken = default)
            => GetCompanyDraftAsync<Integrator>(id, cancellationToken);
    }
}
