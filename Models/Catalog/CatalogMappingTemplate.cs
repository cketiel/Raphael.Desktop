using System.Collections.Generic;

namespace Raphael.Desktop.Models.Catalog
{
    /// <summary>
    /// How one source spreadsheet's columns line up with our catalog fields.
    /// </summary>
    /// <remarks>
    /// A file and not code, for the same reason the trip import templates are files: the
    /// external source will rename a column sooner or later, and answering that with a text
    /// edit rather than a release is the difference between a morning and a week. Same shape
    /// as <see cref="Csv.CsvMappingTemplate"/>, which this deliberately echoes.
    /// </remarks>
    public class CatalogMappingTemplate
    {
        /// <summary>What to call this template in the dropdown.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Which catalog it feeds: <c>Integrator</c> or <c>Provider</c>. A template pointed at
        /// the wrong one would import hospitals as ambulance companies.
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// The group code, matching a row of the category table: <c>NursingHome</c>,
        /// <c>NemtBroker</c> and so on.
        /// </summary>
        public string CategoryCode { get; set; } = string.Empty;

        /// <summary>
        /// Column names that identify the header row.
        /// </summary>
        /// <remarks>
        /// ⚠️ None of the six source files puts its header on the first row: each opens with a
        /// title, a record count, the source and two or three caveats, and the real header
        /// turns up on row 7 or 8. The reader scans down until it finds a row holding at least
        /// two of these, which also means a file that gains a caveat line still imports.
        /// </remarks>
        public List<string> HeaderTokens { get; set; } = new();

        /// <summary>Column heading in the file to our field name, like the CSV templates.</summary>
        public Dictionary<string, string> ColumnMappings { get; set; } = new();
    }
}
