using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// What the import screen needs to know about whichever catalog it is filling.
    /// </summary>
    /// <remarks>
    /// The screen itself is the same for both: pick a file, pick a template, look at the rows,
    /// send them in batches. Only the row shape differs, and this is where that difference
    /// lives — so there is one import screen, not two drifting copies of one.
    /// </remarks>
    public interface ICatalogImportTarget
    {
        /// <summary>"Integrator" or "Provider", matching the templates' <c>Kind</c>.</summary>
        string Kind { get; }

        /// <summary>Sends one batch and returns the verdict per row.</summary>
        Task<CatalogImportResultDto> SendAsync(
            Guid batchId,
            string sourceFile,
            int categoryId,
            IReadOnlyList<CatalogRawRow> rows,
            CancellationToken cancellationToken);
    }

    /// <summary>Fills the catalog of entities that could send us trips.</summary>
    public sealed class CatalogIntegratorImportTarget : ICatalogImportTarget
    {
        private readonly ICatalogIntegratorService _catalog;

        public CatalogIntegratorImportTarget(ICatalogIntegratorService catalog)
        {
            _catalog = catalog;
        }

        public string Kind => "Integrator";

        public Task<CatalogImportResultDto> SendAsync(
            Guid batchId,
            string sourceFile,
            int categoryId,
            IReadOnlyList<CatalogRawRow> rows,
            CancellationToken cancellationToken)
        {
            var request = new CatalogImportRequestDto<CatalogIntegratorImportRowDto>
            {
                ImportBatchId = batchId,
                SourceFile = sourceFile,
                CategoryId = categoryId
            };

            foreach (var row in rows)
            {
                request.Rows.Add(new CatalogIntegratorImportRowDto
                {
                    RowNumber = row.RowNumber,
                    Name = row.Get("Name"),
                    Address = row.Get("Address"),
                    City = row.Get("City"),
                    County = row.Get("County"),
                    State = row.Get("State"),
                    Zip = row.Get("Zip"),
                    Phone = row.Get("Phone"),
                    Email = row.Get("Email"),
                    Website = row.Get("Website"),
                    ContactName = row.Get("ContactName"),
                    FacilityType = row.Get("FacilityType"),
                    Beds = CatalogFieldParser.Int(row.Get("Beds")),
                    ChainName = row.Get("ChainName"),
                    Latitude = CatalogFieldParser.Double(row.Get("Latitude")),
                    Longitude = CatalogFieldParser.Double(row.Get("Longitude"))
                });
            }

            return _catalog.ImportAsync(request, cancellationToken);
        }
    }

    /// <summary>Fills the catalog of entities that could carry out trips.</summary>
    public sealed class CatalogProviderImportTarget : ICatalogImportTarget
    {
        private readonly ICatalogProviderService _catalog;

        public CatalogProviderImportTarget(ICatalogProviderService catalog)
        {
            _catalog = catalog;
        }

        public string Kind => "Provider";

        public Task<CatalogImportResultDto> SendAsync(
            Guid batchId,
            string sourceFile,
            int categoryId,
            IReadOnlyList<CatalogRawRow> rows,
            CancellationToken cancellationToken)
        {
            var request = new CatalogImportRequestDto<CatalogProviderImportRowDto>
            {
                ImportBatchId = batchId,
                SourceFile = sourceFile,
                CategoryId = categoryId
            };

            foreach (var row in rows)
            {
                request.Rows.Add(new CatalogProviderImportRowDto
                {
                    RowNumber = row.RowNumber,
                    Name = row.Get("Name"),
                    Address = row.Get("Address"),
                    City = row.Get("City"),
                    County = row.Get("County"),
                    State = row.Get("State"),
                    Zip = row.Get("Zip"),
                    Phone = row.Get("Phone"),
                    Email = row.Get("Email"),
                    Website = row.Get("Website"),
                    ContactName = row.Get("ContactName"),
                    Npi = row.Get("Npi"),
                    IsPrimaryNemt = CatalogFieldParser.Bool(row.Get("IsPrimaryNemt")),
                    SourceUpdatedOn = CatalogFieldParser.Date(row.Get("SourceUpdatedOn")),
                    EmsLicense = row.Get("EmsLicense"),
                    ServiceLevel = row.Get("ServiceLevel"),
                    LicenseExpiresOn = CatalogFieldParser.Date(row.Get("LicenseExpiresOn")),
                    PlanSegment = row.Get("PlanSegment"),
                    CoverageArea = row.Get("CoverageArea"),
                    ProviderContact = row.Get("ProviderContact"),
                    EvidenceNote = row.Get("EvidenceNote"),
                    Latitude = CatalogFieldParser.Double(row.Get("Latitude")),
                    Longitude = CatalogFieldParser.Double(row.Get("Longitude"))
                });
            }

            return _catalog.ImportAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Turns what a spreadsheet cell says into the type the field needs.
    /// </summary>
    /// <remarks>
    /// Everything here is forgiving on purpose. A value we cannot read becomes null, never an
    /// exception: one unreadable bed count must not reject a row whose name, phone and address
    /// are perfectly good.
    /// </remarks>
    public static class CatalogFieldParser
    {
        public static int? Int(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var digits = new string(Array.FindAll(value.ToCharArray(), char.IsDigit));

            return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        public static double? Double(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Invariant first, then the operator's own culture: a file written elsewhere uses
            // a dot, one saved from a Spanish Excel uses a comma, and both are plausible here.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var invariant))
            {
                return invariant;
            }

            return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var local)
                ? local
                : null;
        }

        /// <summary>Reads "Sí", "Si", "Yes", "True", "1" and their negatives.</summary>
        public static bool? Bool(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var text = value.Trim().ToUpperInvariant();

            return text switch
            {
                "SÍ" or "SI" or "YES" or "Y" or "TRUE" or "1" or "X" => true,
                "NO" or "N" or "FALSE" or "0" => false,
                _ => null
            };
        }

        /// <summary>
        /// Reads a date, including the bare serial number Excel sometimes hands over.
        /// </summary>
        /// <remarks>
        /// ⚠️ The NEMT file's "Actualizado" column arrives as <c>46013.5</c> — the number of
        /// days since 1899-12-30, which is how Excel stores a date that was never given a date
        /// format. Read as text it is meaningless; read as a number it is a real date. Getting
        /// this wrong does not fail, it just writes the wrong year.
        /// </remarks>
        public static DateTime? Date(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var invariant))
            {
                return invariant;
            }

            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var local))
            {
                return local;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                && serial > 0
                && serial < 100_000)
            {
                try
                {
                    return DateTime.FromOADate(serial);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
