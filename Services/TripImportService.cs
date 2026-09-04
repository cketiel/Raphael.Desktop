using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Csv;

namespace Raphael.Desktop.Services
{
    /// <summary>Where an import has got to, for the progress bar.</summary>
    public class TripImportProgress
    {
        public string Stage { get; set; } = string.Empty;
        public int Completed { get; set; }
        public int Total { get; set; }
    }

    /// <summary>One row as it ended up: what was sent, and what the server said about it.</summary>
    /// <remarks>
    /// This is what the preview grid shows once an import finishes. A rejected row needs to be
    /// findable in the original file, so it carries the broker's own TripId and the reason in
    /// business language, not a stack trace.
    /// </remarks>
    public class TripImportRow
    {
        public string? TripId { get; set; }
        public string? Status { get; set; }
        public string? Patient { get; set; }
        public DateTime? Date { get; set; }
        public TimeSpan? FromTime { get; set; }
        public TimeSpan? ToTime { get; set; }
        public string? Pickup { get; set; }
        public string? Dropoff { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>What a whole file came to.</summary>
    public class TripImportOutcome
    {
        public List<TripImportRow> Rows { get; } = new List<TripImportRow>();

        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int FailedCount { get; set; }

        /// <summary>How many HTTP requests the whole import cost. The reason this class exists.</summary>
        public int RequestCount { get; set; }

        /// <summary>True when the import stopped early. The rows already stored stay stored.</summary>
        public bool Aborted { get; set; }

        public string? AbortReason { get; set; }

        public int StoredCount => CreatedCount + UpdatedCount;

        /// <summary>The broker identifiers of every row that did not go in, for the message box.</summary>
        public IEnumerable<string> FailedTripIds =>
            Rows.Where(r => r.Status == TripImportStatus.Failed)
                .Select(r => r.TripId ?? "(no TripId)");
    }

    /// <summary>
    /// Imports a broker's CSV file with a handful of requests instead of thousands.
    /// </summary>
    /// <remarks>
    /// The old import walked the file with five to ten threads and each row cost up to six
    /// requests of its own — two geocodings, a patient lookup, a patient insert, the trip and
    /// the history row. Four hundred trips came to roughly two thousand four hundred requests,
    /// and the shared host reads a burst like that from one address as an attack: it withdraws
    /// the application's permissions, the connection drops, and part of the file never arrives.
    ///
    /// <para>
    /// Three things changed. Every address in the file is resolved in one batch instead of two
    /// requests per row. Every row is mapped in memory, with no network at all. And the rows go
    /// up in chunks, <b>one request in flight at a time</b>, to an endpoint that resolves the
    /// patient and the space type itself. The same four hundred trips now cost about six
    /// requests.
    /// </para>
    ///
    /// <para>
    /// Nothing here may be made concurrent again. The concurrency is the fault.
    /// </para>
    /// </remarks>
    public class TripImportService : ITripImportService
    {
        /// <summary>
        /// Rows per request.
        /// </summary>
        /// <remarks>
        /// Not a size limit — a hundred rows measured 171 KB, far under anything IIS refuses.
        /// It is the response time that binds: each row opens its own transaction, and a chunk
        /// has to finish inside <see cref="ImportTimeout"/> on a shared host. Smaller chunks
        /// also mean finer progress and less to repeat when one is refused.
        /// </remarks>
        private const int ChunkSize = 100;

        /// <summary>Addresses per geocoding request. The server refuses more than 500.</summary>
        private const int GeocodeChunkSize = 400;

        /// <summary>
        /// A breath between chunks.
        /// </summary>
        /// <remarks>
        /// Requests are already sequential, so this is not what keeps the burst down. It is
        /// there so a ten-thousand-row file — a hundred chunks — never looks like a machine
        /// hammering the door as fast as it can answer.
        /// </remarks>
        private const int PauseBetweenChunksMs = 250;

        /// <summary>
        /// How long one chunk may take.
        /// </summary>
        /// <remarks>
        /// The default hundred seconds is too tight: a hundred trips, each in its own
        /// transaction, on a shared host, is not a fast request and should not be.
        /// </remarks>
        private static readonly TimeSpan ImportTimeout = TimeSpan.FromMinutes(5);

        /// <summary>
        /// What to wait before trying a refused chunk again.
        /// </summary>
        /// <remarks>
        /// A 429 or a 503 here is the shared host saying it has had enough, so the answer is to
        /// wait rather than to push. Retrying is safe: the server matches rows on the broker's
        /// TripId, so a row that already went in is updated, never duplicated.
        /// </remarks>
        private static readonly TimeSpan[] RetryDelays =
        {
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(45)
        };

        private readonly HttpClient _httpClient;
        private readonly IRoutingApiService _routing;

        public TripImportService()
            : this(new RoutingApiService())
        {
        }

        public TripImportService(IRoutingApiService routing)
        {
            _routing = routing;
            _httpClient = ApiClientFactory.Create();
            _httpClient.Timeout = ImportTimeout;
        }

        /// <summary>
        /// Maps a whole file and stores it, reporting what happened to every row.
        /// </summary>
        /// <param name="records">The rows as they were read from the CSV.</param>
        /// <param name="fundingSource">The funding source chosen on the import screen.</param>
        /// <param name="isSaferide">True when the file has no coordinates and they must be looked up.</param>
        /// <param name="csvType">Which broker's layout the file follows.</param>
        /// <param name="mapper">Used only for its pure mapping; it makes no calls of its own here.</param>
        public async Task<TripImportOutcome> ImportAsync(
            List<CsvTripRawModel> records,
            FundingSource fundingSource,
            bool isSaferide,
            CsvType csvType,
            CsvTripMapper mapper,
            IProgress<TripImportProgress> progress,
            CancellationToken cancellationToken = default)
        {
            var outcome = new TripImportOutcome();

            if (records == null || records.Count == 0) return outcome;

            // 1. Every address in the file, resolved in one or two requests.
            var coordinates = await ResolveCoordinatesAsync(
                records, isSaferide, outcome, progress, cancellationToken);

            // 2. Map in memory. A row that cannot be mapped is reported here and never sent.
            var items = new List<TripImportItemDto>(records.Count);
            var rowsBySentIndex = new List<TripImportRow>(records.Count);

            foreach (var record in records)
            {
                try
                {
                    var item = mapper.MapToImportItem(record, isSaferide, csvType, coordinates);
                    items.Add(item);
                    rowsBySentIndex.Add(ToRow(item));
                }
                catch (Exception ex)
                {
                    outcome.Rows.Add(new TripImportRow
                    {
                        TripId = record.RideId,
                        Status = TripImportStatus.Failed,
                        Patient = record.PatientFullName ?? $"{record.PatientFirstName} {record.PatientLastName}".Trim(),
                        Pickup = CsvTripMapper.BuildPickupAddress(record),
                        Dropoff = CsvTripMapper.BuildDropoffAddress(record),
                        Reason = ex.Message
                    });
                    outcome.FailedCount++;
                }
            }

            if (items.Count == 0) return outcome;

            // 3. Appointment or Return, decided across the whole file before anything is sent.
            AssignTripTypes(items);

            // 4. Up in chunks, one request at a time.
            await SendAsync(items, rowsBySentIndex, fundingSource, outcome, progress, cancellationToken);

            return outcome;
        }

        /// <summary>
        /// Resolves every distinct address in the file in one batch, or a few.
        /// </summary>
        /// <remarks>
        /// Distinct is what makes the saving: a day's file names the same dozen clinics on
        /// nearly every row, so eight hundred addresses are usually two or three hundred
        /// lookups, and the server has most of them cached already.
        /// </remarks>
        private async Task<Dictionary<string, Coordinates>> ResolveCoordinatesAsync(
            List<CsvTripRawModel> records,
            bool isSaferide,
            TripImportOutcome outcome,
            IProgress<TripImportProgress> progress,
            CancellationToken cancellationToken)
        {
            var resolved = new Dictionary<string, Coordinates>(StringComparer.Ordinal);

            // Ride2md and its like carry their own coordinates. Asking would be paying twice.
            if (!isSaferide) return resolved;

            var addresses = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var record in records)
            {
                foreach (var address in new[]
                         {
                             CsvTripMapper.BuildPickupAddress(record),
                             CsvTripMapper.BuildDropoffAddress(record)
                         })
                {
                    if (!string.IsNullOrWhiteSpace(address) && seen.Add(address))
                    {
                        addresses.Add(address);
                    }
                }
            }

            var done = 0;

            foreach (var chunk in Chunk(addresses, GeocodeChunkSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var results = await _routing.GeocodeBatchAsync(chunk);
                outcome.RequestCount++;

                foreach (var result in results)
                {
                    if (result != null && result.IsUsable && result.Latitude.HasValue && result.Longitude.HasValue)
                    {
                        resolved[result.Address] = new Coordinates
                        {
                            Latitude = result.Latitude.Value,
                            Longitude = result.Longitude.Value
                        };
                    }
                }

                done += chunk.Count;

                progress?.Report(new TripImportProgress
                {
                    Stage = "Geocoding",
                    Completed = done,
                    Total = addresses.Count
                });
            }

            return resolved;
        }

        /// <summary>Sends the mapped rows, one chunk per request, and records what came back.</summary>
        private async Task SendAsync(
            List<TripImportItemDto> items,
            List<TripImportRow> rowsBySentIndex,
            FundingSource fundingSource,
            TripImportOutcome outcome,
            IProgress<TripImportProgress> progress,
            CancellationToken cancellationToken)
        {
            var sent = 0;
            var first = true;

            for (var offset = 0; offset < items.Count; offset += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!first)
                {
                    await Task.Delay(PauseBetweenChunksMs, cancellationToken);
                }

                first = false;

                var count = Math.Min(ChunkSize, items.Count - offset);
                var chunk = items.GetRange(offset, count);

                var request = new TripImportRequestDto
                {
                    FundingSourceId = fundingSource.Id,
                    Items = chunk
                };

                TripImportResultDto result;

                try
                {
                    result = await PostChunkAsync(request, outcome, cancellationToken);
                }
                catch (Exception ex)
                {
                    // The chunks already stored stay stored, and re-importing the file is safe,
                    // so stopping is better than pushing at a host that is refusing us.
                    outcome.Aborted = true;
                    outcome.AbortReason = ex.Message;

                    // Worded for both halves at once, because they are not the same. The rows
                    // after this chunk were certainly never sent; the rows inside it were sent
                    // and the answer never arrived, so the server may well have stored them.
                    // Saying "not imported" of those would send a dispatcher hunting for trips
                    // that are already in the system.
                    for (var i = offset; i < items.Count; i++)
                    {
                        var row = rowsBySentIndex[i];
                        row.Status = TripImportStatus.Failed;
                        row.Reason = "The import stopped here, so this row is not confirmed. "
                                   + "Import the same file again to settle it. " + ex.Message;
                        outcome.Rows.Add(row);
                        outcome.FailedCount++;
                    }

                    return;
                }

                for (var i = 0; i < count; i++)
                {
                    var row = rowsBySentIndex[offset + i];
                    var itemResult = i < result.Results.Count ? result.Results[i] : null;

                    row.Status = itemResult?.Status ?? TripImportStatus.Failed;
                    row.Reason = itemResult?.Message;

                    outcome.Rows.Add(row);
                }

                outcome.CreatedCount += result.CreatedCount;
                outcome.UpdatedCount += result.UpdatedCount;
                outcome.FailedCount += result.FailedCount;

                sent += count;

                progress?.Report(new TripImportProgress
                {
                    Stage = "Importing",
                    Completed = sent,
                    Total = items.Count
                });
            }
        }

        /// <summary>Sends one chunk, waiting and trying again when the host asks us to back off.</summary>
        private async Task<TripImportResultDto> PostChunkAsync(
            TripImportRequestDto request,
            TripImportOutcome outcome,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                outcome.RequestCount++;

                HttpResponseMessage response;

                try
                {
                    response = await _httpClient.PostAsJsonAsync("trips/import", request, cancellationToken);
                }
                catch (HttpRequestException ex) when (attempt < RetryDelays.Length)
                {
                    // A dropped connection is what the host's block looks like from here.
                    await Task.Delay(RetryDelays[attempt], cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"Trip import retrying after a connection failure: {ex.Message}");
                    continue;
                }

                using (response)
                {
                    // 207 and 422 are answers, not failures: the chunk was processed and every
                    // row has its own verdict inside.
                    if (response.IsSuccessStatusCode ||
                        response.StatusCode == HttpStatusCode.MultiStatus ||
                        response.StatusCode == HttpStatusCode.UnprocessableEntity)
                    {
                        var result = await response.Content.ReadFromJsonAsync<TripImportResultDto>(
                            cancellationToken: cancellationToken);

                        if (result != null) return result;

                        throw new InvalidOperationException("The server answered the import with an empty result.");
                    }

                    // 403 is in here because that is how the shared host's block arrives — it is
                    // the host answering, not the API. A real authorization failure costs a
                    // minute of pointless waiting before it is reported, which is the cheaper
                    // mistake of the two.
                    var shouldBackOff =
                        response.StatusCode == HttpStatusCode.TooManyRequests ||
                        response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                        response.StatusCode == HttpStatusCode.Forbidden;

                    if (shouldBackOff && attempt < RetryDelays.Length)
                    {
                        var wait = response.Headers.RetryAfter?.Delta ?? RetryDelays[attempt];
                        await Task.Delay(wait, cancellationToken);
                        continue;
                    }

                    var body = await response.Content.ReadAsStringAsync(cancellationToken);

                    throw new InvalidOperationException(
                        $"The server refused the import with {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
                }
            }
        }

        /// <summary>
        /// Decides Appointment or Return for every row, by looking at a patient's whole day.
        /// </summary>
        /// <remarks>
        /// Grouped on the rider key rather than on a customer id, which is the only change from
        /// the version that used to run in the view: the id did not exist yet on the client, so
        /// this had to wait for every trip to be created and then send a second pass over the
        /// wire. The grouping is the same one — the key is what the patient is matched on.
        ///
        /// <para>
        /// Only <c>Type</c> is written. The Pickup and Dropoff labels keep the type their own row
        /// implied, exactly as before.
        /// </para>
        /// </remarks>
        public static void AssignTripTypes(List<TripImportItemDto> items)
        {
            foreach (var group in items.GroupBy(t => t.RiderId ?? string.Empty))
            {
                var trips = group.OrderBy(t => t.FromTime).ToList();

                if (trips.Count == 1)
                {
                    var trip = trips[0];
                    trip.Type = trip.WillCall ? TripType.Return : TripType.Appointment;
                    continue;
                }

                // The earliest is the journey out; everything after it is a way back.
                trips[0].Type = TripType.Appointment;

                for (var i = 1; i < trips.Count; i++)
                {
                    trips[i].Type = TripType.Return;
                }
            }
        }

        /// <summary>The row as it was sent. The status is filled in from the server's answer.</summary>
        private static TripImportRow ToRow(TripImportItemDto item)
            => new TripImportRow
            {
                TripId = item.TripId,
                Patient = item.CustomerFullName,
                Date = item.Date,
                FromTime = item.FromTime,
                ToTime = item.ToTime,
                Pickup = item.PickupAddress,
                Dropoff = item.DropoffAddress
            };

        private static IEnumerable<List<string>> Chunk(List<string> source, int size)
        {
            for (var offset = 0; offset < source.Count; offset += size)
            {
                yield return source.GetRange(offset, Math.Min(size, source.Count - offset));
            }
        }
    }
}
