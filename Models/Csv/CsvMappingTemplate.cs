using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models.Csv
{
    // Relationship, interaction between classes:
    // CsvTripRawModel: Contains all the raw data from the CSV as strongly typed properties.
    // FundingSourceMappingTemplate - Specifies how to bind the CSV columns to the CsvTripRawModel properties.
    // This model allows you to separate the parsing/mapping logic and facilitates reuse for multiple data sources (other FundingSource).
    public class CsvMappingTemplate
    {
        public string FundingSourceName { get; set; } = string.Empty;
        public Dictionary<string, string> ColumnMappings { get; set; } = new();
    }

}
