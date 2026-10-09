using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// The directory of entities that could carry out trips: NEMT companies, NEMT brokers,
    /// private ambulance operators.
    /// </summary>
    public interface ICatalogProviderService
    {
        Task<CatalogPageDto<CatalogProviderListItemDto>> SearchAsync(
            CatalogSearchRequestDto request, CancellationToken cancellationToken = default);

        Task<CatalogProviderDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

        Task<CatalogProviderDto> CreateAsync(
            CatalogProviderSaveDto request, CancellationToken cancellationToken = default);

        Task<CatalogProviderDto> UpdateAsync(
            int id, CatalogProviderSaveDto request, CancellationToken cancellationToken = default);

        Task<CatalogImportResultDto> ImportAsync(
            CatalogImportRequestDto<CatalogProviderImportRowDto> request,
            CancellationToken cancellationToken = default);

        Task<CatalogGeocodeResultDto> GeocodeAsync(
            System.Collections.Generic.IEnumerable<int> ids,
            CancellationToken cancellationToken = default);

        /// <summary>The provider form, filled in from the catalog row, for the user to confirm.</summary>
        Task<Provider?> GetCompanyDraftAsync(int id, CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="ICatalogProviderService"/>
    public class CatalogProviderService
        : CatalogServiceBase<
            CatalogProviderListItemDto,
            CatalogProviderDto,
            CatalogProviderSaveDto,
            CatalogProviderImportRowDto>,
          ICatalogProviderService
    {
        public CatalogProviderService()
            : base("admin/catalog/providers")
        {
        }

        public Task<Provider?> GetCompanyDraftAsync(int id, CancellationToken cancellationToken = default)
            => GetCompanyDraftAsync<Provider>(id, cancellationToken);
    }
}
