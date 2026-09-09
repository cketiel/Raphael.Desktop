using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Csv;

namespace Raphael.Desktop.Services
{
    /// <summary>Stores a broker's CSV file with a handful of requests instead of thousands.</summary>
    public interface ITripImportService
    {
        /// <summary>
        /// Maps a whole file and stores it, reporting what happened to every row.
        /// </summary>
        /// <param name="records">The rows as they were read from the CSV.</param>
        /// <param name="fundingSource">The funding source chosen on the import screen.</param>
        /// <param name="isSaferide">True when the file has no coordinates and they must be looked up.</param>
        /// <param name="csvType">Which broker's layout the file follows.</param>
        /// <param name="mapper">Used only for its pure mapping; it makes no calls of its own here.</param>
        /// <param name="progress">Reports the geocoding pass and then the import pass.</param>
        /// <param name="cancellationToken">Stops between chunks. What is already stored stays stored.</param>
        Task<TripImportOutcome> ImportAsync(
            List<CsvTripRawModel> records,
            FundingSource fundingSource,
            bool isSaferide,
            CsvType csvType,
            CsvTripMapper mapper,
            IProgress<TripImportProgress> progress,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends corrected rows again, in one request however many there are.
        /// </summary>
        /// <remarks>
        /// Safe by construction: the server matches on the broker's TripId, so a row that turns
        /// out to have gone in already is updated rather than duplicated.
        /// </remarks>
        Task<DTOs.TripImportResultDto> RetryAsync(
            List<DTOs.TripImportItemDto> items,
            FundingSource fundingSource,
            IProgress<TripImportProgress> progress = null,
            CancellationToken cancellationToken = default);
    }
}
