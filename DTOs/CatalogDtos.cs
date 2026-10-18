using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs
{
    // Mirror of Raphael.Backend/Raphael.Shared/DTOs/Catalog/*.cs — RE-027.
    //
    // One file for the whole family, not one per type. That goes against the convention used
    // for the older DTOs and it is deliberate: what is propagated here is a whole contract, and
    // splitting it across eight files is how seven eighths of it get copied. Same reasoning as
    // the routing and import families — see _meta/CONTRACT_MAP.md §2.
    //
    // ⚠️ These are copies. Change one on the server and nothing here fails to compile: the
    // property simply arrives null and the screen quietly shows less than it should.

    /// <summary>One of the groups the catalog is divided into.</summary>
    public class CatalogCategoryDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string NameEs { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }

        /// <summary>How many rows of the catalog fall in this group.</summary>
        public int Count { get; set; }
    }

    public class CountyDto
    {
        public int Id { get; set; }
        public string State { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public override string ToString() => Name;
    }

    /// <summary>A page of results, and how many there were in total.</summary>
    public class CatalogPageDto<T>
    {
        public List<T> Items { get; set; } = new();

        /// <summary>Rows matching the search, not rows in this page.</summary>
        public int TotalCount { get; set; }

        public int PageNumber { get; set; }
        public int PageSize { get; set; }

        public CatalogFacetsDto? Facets { get; set; }

        /// <summary>Near-matches, sent only when the search found nothing.</summary>
        public List<CatalogSuggestionDto> Suggestions { get; set; } = new();
    }

    public class CatalogFacetsDto
    {
        public List<CatalogFacetValueDto> Categories { get; set; } = new();
        public List<CatalogFacetValueDto> Counties { get; set; } = new();
        public List<CatalogFacetValueDto> Cities { get; set; } = new();

        public int InMyCompanyCount { get; set; }
        public int NotInMyCompanyCount { get; set; }
    }

    public class CatalogFacetValueDto
    {
        public int? Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class CatalogSuggestionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? City { get; set; }
    }

    /// <summary>What the screen sends when the user types, filters or turns a page.</summary>
    public class CatalogSearchRequestDto
    {
        public string? Term { get; set; }

        public int? CategoryId { get; set; }
        public int? CountyId { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }

        /// <summary>true for what this company has, false for what it has not, null for both.</summary>
        public bool? InMyCompany { get; set; }

        public bool? HasPhone { get; set; }
        public bool? HasEmail { get; set; }
        public bool? HasWebsite { get; set; }

        public bool IncludeInactive { get; set; }
        public bool IncludeFacets { get; set; }

        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;

        public CatalogSortBy SortBy { get; set; } = CatalogSortBy.Group;
    }

    public enum CatalogSortBy
    {
        Relevance = 0,
        Name = 1,
        City = 2,
        Newest = 3,

        /// <summary>
        /// By group, and inside each group by best match. The default.
        /// </summary>
        /// <remarks>
        /// What lets the grid draw a header per group: a page of fifty can only be grouped on
        /// screen if the rows of one group arrive together, which is the server's job.
        /// </remarks>
        Group = 4
    }

    /// <summary>Why a row has no coordinates. Mirrors the server enum by value.</summary>
    public enum CatalogGeocodeStatus
    {
        NotRequested = 0,
        FromSource = 1,
        Resolved = 2,
        NotFound = 3,
        Failed = 4
    }

    /// <summary>
    /// What the list view model needs of a row, whichever catalog it came from.
    /// </summary>
    /// <remarks>
    /// Exists so the shared view model can label the groups without knowing which of the two
    /// catalogs it is driving.
    /// </remarks>
    public interface ICatalogGroupedRow
    {
        int CategoryId { get; }

        /// <summary>The group's name in the user's language. Set by the view model.</summary>
        string GroupLabel { get; set; }
    }

    // ---- Integrator side ----------------------------------------------------------------

    public class CatalogIntegratorListItemDto : ICatalogGroupedRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public int CategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;

        public string? City { get; set; }
        public string? CountyName { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }

        public string? FacilityType { get; set; }
        public int? Beds { get; set; }

        public bool IsActive { get; set; }
        public bool HasCoordinates { get; set; }

        /// <summary>Whether the signed-in company already added this one.</summary>
        public bool InMyCompany { get; set; }

        public int? MyIntegratorId { get; set; }
        /// <summary>The company had it and uncontracted it (RE-056); MyIntegratorId still points to that row.</summary>
        public bool IsUncontracted { get; set; }

        /// <summary>Total companies that added it. Null unless the caller is the general broker.</summary>
        public int? AddedByCompanyCount { get; set; }

        // ---- The rest of the row, so the grid can show every column ------------------

        public string? Address { get; set; }
        public string? ChainName { get; set; }
        public string? Comments { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int GeocodeStatus { get; set; }

        /// <summary>
        /// The group's name in the user's language, for the grid's group headers.
        /// </summary>
        /// <remarks>
        /// Filled by this application and never sent by the server, which has no idea which
        /// language anybody is reading in. It travels on the row because WPF groups by a
        /// property of the item and nothing else.
        /// </remarks>
        public string GroupLabel { get; set; } = string.Empty;
    }

    public class CatalogIntegratorDto
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }
        public string? City { get; set; }
        public int? CountyId { get; set; }
        public string? CountyName { get; set; }
        public string? CountyRaw { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }

        public string? FacilityType { get; set; }
        public int? Beds { get; set; }
        public string? ChainName { get; set; }

        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int GeocodeStatus { get; set; }
        public DateTime? GeocodedAtUtc { get; set; }

        public string? SourceFile { get; set; }
        public int? SourceRow { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByUserName { get; set; }
        public int? CreatedByProviderId { get; set; }
        public string? CreatedByProviderName { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
        public int? UpdatedByUserId { get; set; }
        public string? UpdatedByUserName { get; set; }
        public int? UpdatedByProviderId { get; set; }
        public string? UpdatedByProviderName { get; set; }

        public bool InMyCompany { get; set; }
        public int? MyIntegratorId { get; set; }
        /// <summary>The company had it and uncontracted it (RE-056); MyIntegratorId still points to that row.</summary>
        public bool IsUncontracted { get; set; }
        public int? AddedByCompanyCount { get; set; }
    }

    /// <summary>What the edit dialog sends. No audit fields: the server takes those from the token.</summary>
    public class CatalogIntegratorSaveDto
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? City { get; set; }
        public int? CountyId { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }
        public string? FacilityType { get; set; }
        public int? Beds { get; set; }
        public string? ChainName { get; set; }
        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CatalogIntegratorImportRowDto
    {
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }
        public string? FacilityType { get; set; }
        public int? Beds { get; set; }
        public string? ChainName { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    // ---- Provider side ------------------------------------------------------------------

    public class CatalogProviderListItemDto : ICatalogGroupedRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public int CategoryId { get; set; }
        public string CategoryCode { get; set; } = string.Empty;

        public string? City { get; set; }
        public string? CountyName { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }

        public string? Npi { get; set; }
        public string? ServiceLevel { get; set; }
        public DateTime? LicenseExpiresOn { get; set; }

        public bool IsActive { get; set; }
        public bool HasCoordinates { get; set; }

        public bool InMyCompany { get; set; }
        public int? MyProviderId { get; set; }
        /// <summary>The company had it and uncontracted it (RE-056); MyProviderId still points to that row.</summary>
        public bool IsUncontracted { get; set; }
        public int? AddedByCompanyCount { get; set; }

        // ---- The rest of the row, so the grid can show every column ------------------

        public string? Address { get; set; }
        public string? Comments { get; set; }
        public bool? IsPrimaryNemt { get; set; }
        public DateTime? SourceUpdatedOn { get; set; }
        public string? EmsLicense { get; set; }
        public string? PlanSegment { get; set; }
        public string? CoverageArea { get; set; }
        public string? ProviderContact { get; set; }
        public string? EvidenceNote { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int GeocodeStatus { get; set; }

        /// <summary>The group's name in the user's language. See the integrator twin.</summary>
        public string GroupLabel { get; set; } = string.Empty;
    }

    public class CatalogProviderDto
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public string Name { get; set; } = string.Empty;

        public string? Address { get; set; }
        public string? City { get; set; }
        public int? CountyId { get; set; }
        public string? CountyName { get; set; }
        public string? CountyRaw { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }

        public string? Npi { get; set; }
        public bool? IsPrimaryNemt { get; set; }
        public DateTime? SourceUpdatedOn { get; set; }
        public string? EmsLicense { get; set; }
        public string? ServiceLevel { get; set; }
        public DateTime? LicenseExpiresOn { get; set; }
        public string? PlanSegment { get; set; }
        public string? CoverageArea { get; set; }
        public string? ProviderContact { get; set; }
        public string? EvidenceNote { get; set; }

        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public int GeocodeStatus { get; set; }
        public DateTime? GeocodedAtUtc { get; set; }

        public string? SourceFile { get; set; }
        public int? SourceRow { get; set; }

        public DateTime CreatedAtUtc { get; set; }
        public int? CreatedByUserId { get; set; }
        public string? CreatedByUserName { get; set; }
        public int? CreatedByProviderId { get; set; }
        public string? CreatedByProviderName { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
        public int? UpdatedByUserId { get; set; }
        public string? UpdatedByUserName { get; set; }
        public int? UpdatedByProviderId { get; set; }
        public string? UpdatedByProviderName { get; set; }

        public bool InMyCompany { get; set; }
        public int? MyProviderId { get; set; }
        /// <summary>The company had it and uncontracted it (RE-056); MyProviderId still points to that row.</summary>
        public bool IsUncontracted { get; set; }
        public int? AddedByCompanyCount { get; set; }
    }

    public class CatalogProviderSaveDto
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string? City { get; set; }
        public int? CountyId { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }
        public string? Npi { get; set; }
        public bool? IsPrimaryNemt { get; set; }
        public DateTime? SourceUpdatedOn { get; set; }
        public string? EmsLicense { get; set; }
        public string? ServiceLevel { get; set; }
        public DateTime? LicenseExpiresOn { get; set; }
        public string? PlanSegment { get; set; }
        public string? CoverageArea { get; set; }
        public string? ProviderContact { get; set; }
        public string? EvidenceNote { get; set; }
        public string? Comments { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CatalogProviderImportRowDto
    {
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? ContactName { get; set; }
        public string? Npi { get; set; }
        public bool? IsPrimaryNemt { get; set; }
        public DateTime? SourceUpdatedOn { get; set; }
        public string? EmsLicense { get; set; }
        public string? ServiceLevel { get; set; }
        public DateTime? LicenseExpiresOn { get; set; }
        public string? PlanSegment { get; set; }
        public string? CoverageArea { get; set; }
        public string? ProviderContact { get; set; }
        public string? EvidenceNote { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    // ---- Import and geocoding -------------------------------------------------------------

    public class CatalogImportRequestDto<T>
    {
        public Guid ImportBatchId { get; set; }
        public string? SourceFile { get; set; }
        public int CategoryId { get; set; }
        public List<T> Rows { get; set; } = new();
    }

    /// <summary>
    /// What happened to the batch. One entry per distinct entity, not per row of the file:
    /// a file can name the same institution several times.
    /// </summary>
    public class CatalogImportResultDto
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Failed { get; set; }

        /// <summary>Rows folded into another row of the same file.</summary>
        public int Merged { get; set; }

        /// <summary>Rows with no name — the blank lines the source files are full of.</summary>
        public int Skipped { get; set; }

        public List<CatalogImportRowResultDto> Rows { get; set; } = new();
    }

    public class CatalogImportRowResultDto
    {
        /// <summary>The first row of the file that described this entity.</summary>
        public int RowNumber { get; set; }

        public int? Id { get; set; }
        public CatalogImportOutcome Outcome { get; set; }

        /// <summary>How many rows of the file described this entity. One, normally.</summary>
        public int MergedRowCount { get; set; } = 1;

        /// <summary>The row numbers, when there was more than one.</summary>
        public List<int>? MergedRowNumbers { get; set; }

        /// <summary>A code, not a sentence. This app owns the wording — see CatalogProblems.</summary>
        public string? ErrorCode { get; set; }

        public string? Field { get; set; }
    }

    public enum CatalogImportOutcome
    {
        Created = 0,
        Updated = 1,
        Rejected = 2
    }

    public class CatalogGeocodeRequestDto
    {
        public List<int> Ids { get; set; } = new();
    }

    public class CatalogGeocodeResultDto
    {
        public int Resolved { get; set; }
        public int NotFound { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int? RemainingDailyQuota { get; set; }
    }
}
