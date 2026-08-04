using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsvHelper.Configuration;
using CsvHelper;
using System.Globalization;
using System.Reflection;

namespace Raphael.Desktop.Models.Csv
{


    public sealed class CsvTripRawModelMap : ClassMap<CsvTripRawModel>
    {
        public CsvTripRawModelMap(
            Dictionary<string, int> headerIndices,
            Dictionary<string, string> csvToPropertyMappings) 
        {
            foreach (var mappingEntry in csvToPropertyMappings)
            {
                string csvHeaderName = mappingEntry.Key;
                string propertyName = mappingEntry.Value;

                if (headerIndices.TryGetValue(csvHeaderName, out int actualColumnIndex)) 
                {
                    //PropertyInfo? propertyInfo = typeof(CsvTripRawModel).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    var propertyInfo = typeof(CsvTripRawModel).GetProperty(propertyName);
                    if (propertyInfo != null)
                    {
                        //Map(typeof(CsvTripRawModel), propertyInfo).Index(actualColumnIndex).Name(csvHeaderName);
                        Map(typeof(CsvTripRawModel), propertyInfo).Index(actualColumnIndex);
                    }
                    else
                    {
                        Console.WriteLine($"Warning: The property '{propertyName}' was not found in CsvTripRawModel for CSV header '{csvHeaderName}'.");
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: The CSV header '{csvHeaderName}' (defined in JSON mapping) was not found in the read CSV file.");
                }
            }
        }
    }
}
