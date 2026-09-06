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
using Raphael.Desktop.Models.Import;
using Raphael.Desktop.Services.Import;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Where an import has got to — for the bar, and for the running account beside it.
    /// </summary>
    /// <remarks>
    /// One channel, two readers. <see cref="Completed"/> and <see cref="Total"/> move the bar;
    /// <see cref="Message"/>, when there is one, is a line for the detail panel. A report can
    /// carry either or both, which is what lets "sending chunk 3 of 9" advance the bar and say so
    /// in the same breath.
    /// </remarks>
    public class TripImportProgress
    {
        public string Stage { get; set; } = string.Empty;
        public int Completed { get; set; }
        public int Total { get; set; }

        /// <summary>A line for the detail panel. Null when this report only moves the bar.</summary>
        public string? Message { get; set; }

        public Models.Import.ImportSeverity Severity { get; set; } = Models.Import.ImportSeverity.Info;

        // ---- one request's own bar --------------------------------------------------------
        //
        // Declared up front with the work it holds, so the overall figure is the sum of them and
        // never resets. The single bar this replaced ran 0 to 100 for geocoding and then 0 to 100
        // again for the trips: on a file costing three requests it reached the end twice.

        /// <summary>Identity of the request being reported. Null when this is not about one.</summary>
        public string? StepKey { get; set; }

        public string? StepLabel { get; set; }

        public int StepTotal { get; set; }

        public int StepCompleted { get; set; }

        public Models.Import.ImportStepState? StepState { get; set; }

        /// <summary>What the request came to, once it has.</summary>
        public string? StepSummary { get; set; }
    }

    /// <summary>One row as it ended up: what was sent, and what the server said about it.</summary>
    /// <remarks>
    /// This is what the preview grid shows once an import finishes. A rejected row needs to be
    /// findable in the original file, so it carries the broker's own TripId and the reason in
    /// business language, not a stack trace.
    /// </remarks>
    public class TripImportRow
    {
        /// <summary>
        /// Which line of the file this was, counted from the first row of data.
        /// </summary>
        /// <remarks>
        /// Carried the whole way through so the export of failures can copy the original lines
        /// instead of rebuilding them. A rebuilt line is a different file: the columns come back
        /// in our order, dates in our format, and quoting where we would have quoted. The office
        /// re-imports what it exported, so it has to be the same file with fewer rows in it.
        /// </remarks>
        public int SourceIndex { get; set; }

        /// <summary>The row as it was sent, so a rejected one can be corrected and sent again.</summary>
        public TripImportItemDto? Item { get; set; }

        /// <summary>The server's verdict, whole. Null for a row this application refused itself.</summary>
        public TripImportItemResultDto? Result { get; set; }

        /// <summary>Set when we refused it ourselves, before anything was sent.</summary>
        public string? LocalCode { get; set; }

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

        /// <summary>
        /// Rows that went in but are worth looking at — an address that would not resolve, say.
        /// </summary>
        /// <remarks>
        /// Kept apart from <see cref="Rows"/> on purpose. A warning is not a failure and must not
        /// be counted as one: the trip is in the system and the day can be dispatched. Home
        /// already has a filter for trips missing coordinates, which is where these are picked up
        /// afterwards; refusing them here would have taken away a workflow the office relies on.
        /// </remarks>
        public List<TripImportRow> Warnings { get; } = new List<TripImportRow>();

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

            Say(progress, "Reading", ImportSeverity.Info,
                string.Format(Text("import.log.FileRead"), records.Count));

            // ⚠️ Every request is declared BEFORE the first one is sent, and that is the whole
            // point. Declaring them as they come round meant the denominator grew mid-run: the
            // geocoding bar reached 100%, the batches were then announced, and the overall figure
            // fell back to 57%. A percentage that goes backwards is worse than no percentage.
            var addresses = DistinctAddresses(records, isSaferide);

            DeclareRequests(progress, addresses.Count, records.Count);

            // 1. Every address in the file, resolved in one or two requests.
            var coordinates = await ResolveCoordinatesAsync(
                addresses, outcome, progress, cancellationToken);

            // 2. Map in memory. A row that cannot be mapped is reported here and never sent.
            var items = new List<TripImportItemDto>(records.Count);
            var rowsBySentIndex = new List<TripImportRow>(records.Count);

            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];

                try
                {
                    var item = mapper.MapToImportItem(record, isSaferide, csvType, coordinates);

                    items.Add(item);
                    rowsBySentIndex.Add(ToRow(index, item));
                }
                catch (Exception ex)
                {
                    outcome.Rows.Add(new TripImportRow
                    {
                        SourceIndex = index,
                        LocalCode = ImportLocalCode.MappingFailed,
                        TripId = record.RideId,
                        Status = TripImportStatus.Failed,
                        Patient = record.PatientFullName ?? $"{record.PatientFirstName} {record.PatientLastName}".Trim(),
                        Pickup = CsvTripMapper.BuildPickupAddress(record),
                        Dropoff = CsvTripMapper.BuildDropoffAddress(record),
                        Reason = ex.Message
                    });

                    outcome.FailedCount++;

                    Say(progress, "Mapping", ImportSeverity.Error,
                        string.Format(Text("import.log.RowUnreadable"), index + 2, record.RideId, ex.Message));
                }
            }

            if (items.Count == 0) return outcome;

            // 3. Appointment or Return, decided across the whole file before anything is sent.
            AssignTripTypes(items);

            Say(progress, "Types", ImportSeverity.Info,
                string.Format(Text("import.log.TypesAssigned"), items.Count));

            // 4. What we can already see is wrong.
            //
            // Four of the server's twelve refusal codes are visible in the file — no TripId, no
            // way of telling the patient apart, a missing required field, an address past the
            // column width — and a row we know will be refused is a round trip nobody needs. It
            // also puts the problem in front of the dispatcher while the file is still in their
            // hand, instead of after the whole thing has run.
            Preflight(items, rowsBySentIndex, outcome, progress);

            if (items.Count == 0)
            {
                Say(progress, "Preflight", ImportSeverity.Error, Text("import.log.NothingToSend"));
                return outcome;
            }

            // 5. Up in chunks, one request at a time.
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
        /// <summary>
        /// Every distinct address in the file, or none when the file carries its own coordinates.
        /// </summary>
        /// <remarks>
        /// Distinct is what makes the saving: a day's file names the same dozen clinics on nearly
        /// every row, so eight hundred addresses are usually two or three hundred lookups, and the
        /// server has most of them cached already.
        ///
        /// <para>
        /// Split out of the pass below so the count is known before anything runs, which is what
        /// lets the geocoding request be declared alongside the batches instead of after them.
        /// </para>
        /// </remarks>
        private static List<string> DistinctAddresses(List<CsvTripRawModel> records, bool isSaferide)
        {
            var addresses = new List<string>();

            // Ride2md and its like carry their own coordinates. Asking would be paying twice.
            if (!isSaferide) return addresses;

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

            return addresses;
        }

        private async Task<Dictionary<string, Coordinates>> ResolveCoordinatesAsync(
            List<string> addresses,
            TripImportOutcome outcome,
            IProgress<TripImportProgress> progress,
            CancellationToken cancellationToken)
        {
            var resolved = new Dictionary<string, Coordinates>(StringComparer.Ordinal);

            if (addresses.Count == 0) return resolved;

            Say(progress, "Geocoding", ImportSeverity.Info,
                string.Format(Text("import.log.GeocodeStart"), addresses.Count));

            Step(progress, GeocodeStepKey, Text("import.step.Geocode"),
                 addresses.Count, 0, ImportStepState.Running);

            var done = 0;
            var unresolved = 0;

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

                Step(progress, GeocodeStepKey, Text("import.step.Geocode"),
                     addresses.Count, done, ImportStepState.Running);
            }

            foreach (var address in addresses)
            {
                if (resolved.ContainsKey(address)) continue;

                unresolved++;

                Say(progress, "Geocoding", ImportSeverity.Warning,
                    string.Format(Text("import.log.AddressUnresolved"), address));
            }

            Say(progress, "Geocoding",
                unresolved == 0 ? ImportSeverity.Success : ImportSeverity.Warning,
                string.Format(Text("import.log.GeocodeDone"), resolved.Count, addresses.Count, unresolved));

            Step(progress, GeocodeStepKey, Text("import.step.Geocode"),
                 addresses.Count, addresses.Count, ImportStepState.Done,
                 string.Format(Text("import.step.GeocodeSummary"), resolved.Count, unresolved));

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

                var chunkNumber = (offset / ChunkSize) + 1;
                var chunkTotal = (items.Count + ChunkSize - 1) / ChunkSize;

                Say(progress, "Sending", ImportSeverity.Info,
                    string.Format(Text("import.log.ChunkSending"), chunkNumber, chunkTotal, count));

                Step(progress, ChunkStepKey(chunkNumber),
                     string.Format(Text("import.step.Batch"), chunkNumber, chunkTotal),
                     count, 0, ImportStepState.Running);

                try
                {
                    result = await PostChunkAsync(request, outcome, progress, cancellationToken);
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
                    Say(progress, "Sending", ImportSeverity.Error,
                        string.Format(Text("import.log.Aborted"), ex.Message));

                    Step(progress, ChunkStepKey(chunkNumber),
                         string.Format(Text("import.step.Batch"), chunkNumber, chunkTotal),
                         count, 0, ImportStepState.Failed, Text("import.step.BatchFailed"));

                    for (var i = offset; i < items.Count; i++)
                    {
                        var row = rowsBySentIndex[i];
                        row.Status = TripImportStatus.Failed;
                        row.LocalCode = ImportLocalCode.NotConfirmed;
                        row.Reason = ex.Message;
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
                    row.Result = itemResult;

                    outcome.Rows.Add(row);

                    if (row.Status == TripImportStatus.Failed)
                    {
                        // ⚠️ Patient and addresses on purpose: this panel is what a dispatcher
                        // opens when a row will not go in, and "row 143 was refused" is not
                        // something anyone can act on. It is a screen and it stays a screen —
                        // nothing here reaches FileLogger. `../CLAUDE.md` §3.
                        Say(progress, "Sending", ImportSeverity.Error,
                            string.Format(
                                Text("import.log.RowRefused"),
                                row.SourceIndex + 2,
                                row.TripId,
                                row.Patient,
                                itemResult?.ErrorCode,
                                itemResult?.Message));
                    }
                }

                outcome.CreatedCount += result.CreatedCount;
                outcome.UpdatedCount += result.UpdatedCount;
                outcome.FailedCount += result.FailedCount;

                sent += count;

                progress?.Report(new TripImportProgress
                {
                    Stage = "Importing",
                    Severity = result.FailedCount == 0 ? ImportSeverity.Success : ImportSeverity.Warning,
                    Message = string.Format(
                        Text("import.log.ChunkDone"),
                        chunkNumber,
                        result.CreatedCount,
                        result.UpdatedCount,
                        result.FailedCount),

                    StepKey = ChunkStepKey(chunkNumber),
                    StepLabel = string.Format(Text("import.step.Batch"), chunkNumber, chunkTotal),
                    StepTotal = count,
                    StepCompleted = count,
                    StepState = ImportStepState.Done,
                    StepSummary = string.Format(
                        Text("import.step.BatchSummary"),
                        result.CreatedCount,
                        result.UpdatedCount,
                        result.FailedCount)
                });
            }
        }

        /// <summary>Sends one chunk, waiting and trying again when the host asks us to back off.</summary>
        private async Task<TripImportResultDto> PostChunkAsync(
            TripImportRequestDto request,
            TripImportOutcome outcome,
            IProgress<TripImportProgress> progress,
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
                    Say(progress, "Sending", ImportSeverity.Warning,
                        string.Format(
                            Text("import.log.RetryConnection"),
                            (int)RetryDelays[attempt].TotalSeconds,
                            attempt + 1,
                            RetryDelays.Length,
                            ex.Message));

                    await Task.Delay(RetryDelays[attempt], cancellationToken);
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

                        Say(progress, "Sending", ImportSeverity.Warning,
                            string.Format(
                                Text("import.log.RetryBackoff"),
                                (int)response.StatusCode,
                                (int)wait.TotalSeconds,
                                attempt + 1,
                                RetryDelays.Length));

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

        /// <summary>
        /// Sends one corrected row, or a handful of them, and reports what came back.
        /// </summary>
        /// <remarks>
        /// The same endpoint as the import, because a corrected row is an import of one row. It
        /// is safe to send: the server matches on the broker's TripId, so a row that turns out to
        /// have gone in already is updated rather than duplicated.
        ///
        /// <para>
        /// Takes a list rather than a single row so that forty corrections cost one request. A
        /// dispatcher fixing a whole file one row at a time is exactly the burst RE-009 removed.
        /// </para>
        /// </remarks>
        public async Task<TripImportResultDto> RetryAsync(
            List<TripImportItemDto> items,
            FundingSource fundingSource,
            IProgress<TripImportProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var outcome = new TripImportOutcome();

            var request = new TripImportRequestDto
            {
                FundingSourceId = fundingSource.Id,
                Items = items
            };

            Say(progress, "Retry", ImportSeverity.Info,
                string.Format(Text("import.log.RetrySending"), items.Count));

            var result = await PostChunkAsync(request, outcome, progress, cancellationToken);

            Say(progress, "Retry",
                result.FailedCount == 0 ? ImportSeverity.Success : ImportSeverity.Warning,
                string.Format(
                    Text("import.log.RetryDone"),
                    result.CreatedCount + result.UpdatedCount,
                    result.FailedCount));

            return result;
        }

        /// <summary>
        /// Pulls out the rows this application can already see will be refused.
        /// </summary>
        /// <remarks>
        /// Every check here mirrors a rule the server states in `INTEGRATION_API_SPEC.md` §6.1,
        /// and answers it with the same code prefixed <c>LOCAL_</c> so a screenshot says who
        /// decided. Getting one wrong is safe in one direction only: a check that is too strict
        /// withholds a row the server would have taken, so when a rule is not certain, it is not
        /// a blocker.
        ///
        /// <para>
        /// Missing coordinates are the case in point. They are a WARNING, not a blocker: the trip
        /// stores fine, Home has a filter for exactly these, and the office fixes them there. A
        /// blocker would have taken away a workflow that already works.
        /// </para>
        /// </remarks>
        private static void Preflight(
            List<TripImportItemDto> items,
            List<TripImportRow> rowsBySentIndex,
            TripImportOutcome outcome,
            IProgress<TripImportProgress> progress)
        {
            var blocked = 0;

            for (var i = items.Count - 1; i >= 0; i--)
            {
                var item = items[i];
                var row = rowsBySentIndex[i];

                var code = ImportPreflight.FirstProblemWith(item);

                if (code == null)
                {
                    if (!ImportPreflight.HasCoordinates(item))
                    {
                        row.LocalCode = ImportLocalCode.NoCoordinates;
                        outcome.Warnings.Add(row);

                        Say(progress, "Preflight", ImportSeverity.Warning,
                            string.Format(Text("import.log.NoCoordinates"), row.SourceIndex + 2, item.TripId));
                    }

                    continue;
                }

                row.Status = TripImportStatus.Failed;
                row.LocalCode = code;

                outcome.Rows.Add(row);
                outcome.FailedCount++;
                blocked++;

                items.RemoveAt(i);
                rowsBySentIndex.RemoveAt(i);

                Say(progress, "Preflight", ImportSeverity.Error,
                    string.Format(
                        Text("import.log.PreflightRefused"),
                        row.SourceIndex + 2,
                        string.IsNullOrWhiteSpace(item.TripId) ? "—" : item.TripId,
                        code));
            }

            Say(progress, "Preflight",
                blocked == 0 ? ImportSeverity.Success : ImportSeverity.Warning,
                string.Format(Text("import.log.PreflightDone"), items.Count, blocked));
        }

        private static string Text(string key) => LocalizationService.Instance[key];

        /// <summary>Names of the requests, so a later report finds the bar it belongs to.</summary>
        private const string GeocodeStepKey = "geocode";

        private static string ChunkStepKey(int number) => "chunk:" + number;

        /// <summary>
        /// Announces every request this import will make, before it makes any of them.
        /// </summary>
        /// <remarks>
        /// The batch count is worked out from the rows read, not from the rows that survive the
        /// preflight, so it can only ever be the same or smaller by the time they are sent. A
        /// denominator that shrinks makes the percentage jump forward, which is untidy; one that
        /// grows makes it go backwards, which is a bug.
        /// </remarks>
        private static void DeclareRequests(
            IProgress<TripImportProgress> progress, int addressCount, int rowCount)
        {
            if (addressCount > 0)
            {
                Step(progress, GeocodeStepKey, Text("import.step.Geocode"),
                     addressCount, 0, ImportStepState.Waiting);
            }

            var batches = (rowCount + ChunkSize - 1) / ChunkSize;

            for (var n = 1; n <= batches; n++)
            {
                var rows = Math.Min(ChunkSize, rowCount - ((n - 1) * ChunkSize));

                Step(progress, ChunkStepKey(n),
                     string.Format(Text("import.step.Batch"), n, batches),
                     rows, 0, ImportStepState.Waiting);
            }
        }

        /// <summary>Declares or updates one request's own bar.</summary>
        private static void Step(
            IProgress<TripImportProgress> progress,
            string key,
            string label,
            int total,
            int completed,
            ImportStepState state,
            string summary = null)
        {
            progress?.Report(new TripImportProgress
            {
                Stage = "Step",
                StepKey = key,
                StepLabel = label,
                StepTotal = total,
                StepCompleted = completed,
                StepState = state,
                StepSummary = summary
            });
        }

        /// <summary>
        /// Puts one line in the detail panel without disturbing the bar.
        /// </summary>
        /// <remarks>
        /// Completed and Total are left at zero, and the view model reads a report with no totals
        /// as "a line, not a position". That is what lets a retry notice appear mid-chunk without
        /// the bar jumping back to the start.
        /// </remarks>
        private static void Say(
            IProgress<TripImportProgress> progress,
            string stage,
            ImportSeverity severity,
            string text)
        {
            progress?.Report(new TripImportProgress
            {
                Stage = stage,
                Severity = severity,
                Message = text
            });
        }

        /// <summary>The row as it was sent. The status is filled in from the server's answer.</summary>
        private static TripImportRow ToRow(int sourceIndex, TripImportItemDto item)
            => new TripImportRow
            {
                SourceIndex = sourceIndex,
                Item = item,
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
