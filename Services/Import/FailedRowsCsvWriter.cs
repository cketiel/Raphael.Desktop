using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace Raphael.Desktop.Services.Import
{
    /// <summary>
    /// Writes the rows that did not go in back out as the file they came from.
    /// </summary>
    /// <remarks>
    /// The office's way out of a bad import is to fix the rows and import them again, so what
    /// comes out has to be something that can go back in. That rules out building a report: it
    /// has to be the broker's own layout, in the broker's own column order, or the second import
    /// fails on the format instead of on the data.
    ///
    /// <para>
    /// So the lines are <b>copied</b>, not rebuilt. <see cref="IParser.RawRecord"/> hands back the
    /// original text of a record — delimiters, quoting, line ending and all — which is the only
    /// way to be sure the file that comes out is the file that went in with rows removed. A
    /// rebuilt line would carry our quoting rules and our date format, and the difference only
    /// shows up when somebody tries to re-import it.
    /// </para>
    ///
    /// <para>
    /// ⚠️ The file it writes holds patient names, addresses and phone numbers. It goes where the
    /// dispatcher asks and nowhere else, and it is never written to a temporary folder, a log or
    /// anything that gets swept up automatically. `../CLAUDE.md` §3.
    /// </para>
    /// </remarks>
    public static class FailedRowsCsvWriter
    {
        /// <summary>
        /// Copies the numbered data rows of <paramref name="sourcePath"/> into a new file.
        /// </summary>
        /// <param name="sourcePath">The CSV that was imported.</param>
        /// <param name="destinationPath">Where to write.</param>
        /// <param name="sourceIndexes">Zero-based indexes of the data rows to keep.</param>
        /// <returns>How many rows were written.</returns>
        public static int Write(string sourcePath, string destinationPath, IEnumerable<int> sourceIndexes)
        {
            var wanted = new HashSet<int>(sourceIndexes);

            if (wanted.Count == 0) return 0;

            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null
            };

            var kept = 0;

            using var reader = new StreamReader(sourcePath);
            using var csv = new CsvReader(reader, configuration);
            using var writer = new StreamWriter(destinationPath, false, new UTF8Encoding(true));

            csv.Read();
            csv.ReadHeader();

            // The header goes out exactly as it came in, including its line ending: the mapping
            // files are keyed on these strings, so a header rewritten even slightly is a file the
            // importer no longer recognises.
            writer.Write(csv.Parser.RawRecord);

            var index = 0;

            while (csv.Read())
            {
                if (wanted.Contains(index))
                {
                    writer.Write(csv.Parser.RawRecord);
                    kept++;
                }

                index++;
            }

            writer.Flush();

            return kept;
        }

        /// <summary>
        /// The name the file gets.
        /// </summary>
        /// <remarks>
        /// Three things, in the order somebody scanning a downloads folder needs them: whose
        /// trips these are, which day they were rejected, and that they were <b>not</b> imported.
        /// The last part is not decoration — a file of trips sitting next to the one that was
        /// imported, with no word on it, is a file somebody eventually imports twice.
        /// </remarks>
        public static string SuggestName(string fundingSourceName, DateTime when, string notImportedWord)
        {
            var safe = string.Join("-", (fundingSourceName ?? "trips").Split(Path.GetInvalidFileNameChars()))
                             .Trim('-', ' ');

            if (string.IsNullOrWhiteSpace(safe)) safe = "trips";

            return $"{safe}_{when:yyyy-MM-dd}_{notImportedWord}.csv";
        }
    }
}
