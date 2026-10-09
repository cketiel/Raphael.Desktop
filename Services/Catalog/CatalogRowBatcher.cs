using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// Cuts the rows of a file into batches without ever splitting one entity across two.
    /// </summary>
    /// <remarks>
    /// The server folds repeated rows of the same entity into one, but it can only fold what
    /// arrives together. Chop the file every hundred rows and a hospital whose two records
    /// straddle the boundary is created by one batch and updated by the next — which is exactly
    /// the "2 updated" nobody could explain on a catalog that started empty.
    ///
    /// <para>
    /// ⚠️ The grouping here only has to be close enough to keep relatives together; the server
    /// still decides what is the same entity. If the two ever disagree the import is still
    /// correct, it just reports an update where it could have reported a merge.
    /// </para>
    /// </remarks>
    public static class CatalogRowBatcher
    {
        /// <summary>
        /// Batches of about <paramref name="batchSize"/> rows, each holding whole entities.
        /// </summary>
        /// <remarks>
        /// A batch can exceed the size when a single entity has more rows than the size allows,
        /// which is the lesser evil: the alternative is splitting it, and the whole point here
        /// is not to.
        /// </remarks>
        public static IEnumerable<IReadOnlyList<CatalogRawRow>> Batch(
            IReadOnlyList<CatalogRawRow> rows,
            int batchSize)
        {
            // Rows of one entity, in the order the file listed them.
            var groups = new Dictionary<string, List<CatalogRawRow>>();
            var order = new List<string>();

            foreach (var row in rows)
            {
                var key = GroupKey(row);

                if (!groups.TryGetValue(key, out var bucket))
                {
                    bucket = new List<CatalogRawRow>();
                    groups[key] = bucket;
                    order.Add(key);
                }

                bucket.Add(row);
            }

            var batch = new List<CatalogRawRow>();

            foreach (var key in order)
            {
                var group = groups[key];

                if (batch.Count > 0 && batch.Count + group.Count > batchSize)
                {
                    yield return batch;
                    batch = new List<CatalogRawRow>();
                }

                batch.AddRange(group);
            }

            if (batch.Count > 0)
            {
                yield return batch;
            }
        }

        /// <summary>
        /// Name and ZIP, upper-cased, unaccented and stripped of anything that is not a letter
        /// or a digit — the same shape the server builds its natural key from.
        /// </summary>
        private static string GroupKey(CatalogRawRow row)
        {
            var builder = new StringBuilder(64);
            Append(builder, row.Get("Name"));
            builder.Append('|');
            Append(builder, row.Get("Zip"));
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var character in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToUpperInvariant(character));
                }
            }
        }
    }
}
