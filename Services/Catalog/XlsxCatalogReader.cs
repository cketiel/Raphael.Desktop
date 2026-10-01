using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using Raphael.Desktop.Models.Catalog;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>One spreadsheet row, already keyed by our field names.</summary>
    public sealed class CatalogRawRow
    {
        /// <summary>Row number in the original file, so a rejection can be pointed at.</summary>
        public int RowNumber { get; init; }

        public Dictionary<string, string> Values { get; init; } = new();

        public string? Get(string field) =>
            Values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
    }

    public sealed class CatalogReadResult
    {
        public List<CatalogRawRow> Rows { get; } = new();

        /// <summary>Which row of the sheet turned out to be the header.</summary>
        public int HeaderRow { get; set; }

        /// <summary>
        /// Columns in the file that the template does not know about. Not an error — shown so
        /// somebody can decide whether the template needs a line adding.
        /// </summary>
        public List<string> UnmappedColumns { get; } = new();

        /// <summary>
        /// Rows skipped because they had no name. The source files are full of spacing rows —
        /// the assisted living sheet has 2,515 of them — and these are counted, not reported
        /// one by one.
        /// </summary>
        public int BlankRowsSkipped { get; set; }
    }

    /// <summary>
    /// Reads one of the source spreadsheets into rows our import can send.
    /// </summary>
    /// <remarks>
    /// ClosedXML is already in this project for exporting trips; this is the first thing that
    /// reads with it. Nothing leaves the machine here — the file is parsed locally and only
    /// the mapped fields are sent, in batches, to the API.
    /// </remarks>
    public static class XlsxCatalogReader
    {
        /// <summary>How far down to look for the header before giving up.</summary>
        /// <remarks>
        /// The six known files put it on row 7 or 8, under a title, a count, the source and a
        /// couple of caveats. Twenty leaves room for a file that grows another paragraph.
        /// </remarks>
        private const int MaxHeaderScanRows = 20;

        /// <summary>How many of a template's header tokens a row must hold to be the header.</summary>
        private const int MinTokenMatches = 2;

        public static CatalogReadResult Read(string path, CatalogMappingTemplate template)
        {
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheets.First();

            var result = new CatalogReadResult();

            var headerRow = FindHeaderRow(sheet, template)
                ?? throw new InvalidOperationException(
                    "The header row was not found in the first "
                    + MaxHeaderScanRows
                    + " rows. Check that the template matches this file.");

            result.HeaderRow = headerRow;

            // Heading text to our field name, built once per file.
            var columnToField = new Dictionary<int, string>();
            var normalizedMappings = template.ColumnMappings
                .GroupBy(kv => Normalize(kv.Key))
                .ToDictionary(g => g.Key, g => g.First().Value);

            var lastColumn = sheet.Row(headerRow).LastCellUsed()?.Address.ColumnNumber ?? 0;

            for (var column = 1; column <= lastColumn; column++)
            {
                var heading = sheet.Cell(headerRow, column).GetString();

                if (string.IsNullOrWhiteSpace(heading))
                {
                    continue;
                }

                if (normalizedMappings.TryGetValue(Normalize(heading), out var field))
                {
                    columnToField[column] = field;
                }
                else
                {
                    result.UnmappedColumns.Add(heading.Trim());
                }
            }

            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow;

            for (var row = headerRow + 1; row <= lastRow; row++)
            {
                var values = new Dictionary<string, string>();

                foreach (var (column, field) in columnToField)
                {
                    var text = sheet.Cell(row, column).GetFormattedString();

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        values[field] = text.Trim();
                    }
                }

                if (!values.TryGetValue("Name", out var name) || string.IsNullOrWhiteSpace(name))
                {
                    result.BlankRowsSkipped++;
                    continue;
                }

                result.Rows.Add(new CatalogRawRow { RowNumber = row, Values = values });
            }

            return result;
        }

        /// <summary>
        /// Finds the row that holds the column headings, which is never the first one.
        /// </summary>
        private static int? FindHeaderRow(IXLWorksheet sheet, CatalogMappingTemplate template)
        {
            if (template.HeaderTokens.Count == 0)
            {
                return null;
            }

            var tokens = template.HeaderTokens.Select(Normalize).ToHashSet();

            for (var row = 1; row <= MaxHeaderScanRows; row++)
            {
                var matches = 0;
                var lastColumn = sheet.Row(row).LastCellUsed()?.Address.ColumnNumber ?? 0;

                for (var column = 1; column <= lastColumn; column++)
                {
                    var text = sheet.Cell(row, column).GetString();

                    if (!string.IsNullOrWhiteSpace(text) && tokens.Contains(Normalize(text)))
                    {
                        matches++;
                    }
                }

                if (matches >= Math.Min(MinTokenMatches, tokens.Count))
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>
        /// Upper-cased, accents removed, whitespace collapsed — so "Teléfono", "TELEFONO" and
        /// "Teléfono " are the same heading.
        /// </summary>
        private static string Normalize(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var lastWasSpace = true;

            foreach (var character in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    if (!lastWasSpace)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }

                    continue;
                }

                builder.Append(char.ToUpperInvariant(character));
                lastWasSpace = false;
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>Loads the templates shipped with the application.</summary>
        public static List<CatalogMappingTemplate> LoadTemplates(string kind)
        {
            var folder = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "Mappings", "Catalog");

            if (!Directory.Exists(folder))
            {
                return new List<CatalogMappingTemplate>();
            }

            var templates = new List<CatalogMappingTemplate>();

            foreach (var file in Directory.GetFiles(folder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var template = System.Text.Json.JsonSerializer
                        .Deserialize<CatalogMappingTemplate>(json,
                            new System.Text.Json.JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                    if (template is not null
                        && string.Equals(template.Kind, kind, StringComparison.OrdinalIgnoreCase))
                    {
                        templates.Add(template);
                    }
                }
                catch (Exception)
                {
                    // A malformed template is one unusable option, not a broken screen. The
                    // others still load and the dropdown simply does not offer this one.
                }
            }

            return templates.OrderBy(t => t.Name).ToList();
        }
    }
}
