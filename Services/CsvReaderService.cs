
using System.Globalization;
using System.IO;
using Raphael.Desktop.Models.Csv;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;


namespace Raphael.Desktop.Services
{
    public enum CsvType
    {
        Saferide,
        Saferide2,
        Ride2md
    }
    public class CsvReaderService
    {
        private readonly string _csvFilePath;
        // private readonly string _jsonFileName;
        public CsvReaderService(string csvFilePath/*, string jsonFileName*/)
        {
            _csvFilePath = csvFilePath;
            //_jsonFileName = jsonFileName;
        }

        public CsvType GetCsvType()
        {
            CsvType result = CsvType.Saferide;
            // Define CsvHelper settings
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = (args) =>
                {
                    Console.WriteLine($"Missing field found. Headers: '{string.Join(", ", args.HeaderNames ?? new string[0])}', Index: {args.Index}, Row: {args.Context.Parser.Row}");
                },
            };

            using var reader = new StreamReader(_csvFilePath);
            using var csv = new CsvHelper.CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var actualCsvHeaders = csv.Context.Reader.HeaderRecord;
            if (actualCsvHeaders == null)
            {
                throw new InvalidOperationException("The CSV file contains no headers or is empty.");
            }
            var header = actualCsvHeaders[0];
            if (string.Equals(header, "rideId", StringComparison.OrdinalIgnoreCase))
            {
                result = CsvType.Saferide;
            }
            else if (string.Equals(header, "MediRoutesClient", StringComparison.OrdinalIgnoreCase))
            {
                result = CsvType.Saferide2;
            }
            else // "Check". if (string.Equals(header, "Check", StringComparison.OrdinalIgnoreCase))
            {
                return CsvType.Ride2md;
            }
            return result;
        }
        public bool IsSaferide()
        {
            // Define CsvHelper settings
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = (args) =>
                {
                    Console.WriteLine($"Missing field found. Headers: '{string.Join(", ", args.HeaderNames ?? new string[0])}', Index: {args.Index}, Row: {args.Context.Parser.Row}");
                },
            };

            using var reader = new StreamReader(_csvFilePath);
            using var csv = new CsvHelper.CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var actualCsvHeaders = csv.Context.Reader.HeaderRecord;
            if (actualCsvHeaders == null)
            {
                throw new InvalidOperationException("The CSV file contains no headers or is empty.");
            }
            var header = actualCsvHeaders[0]; 
            if(string.Equals(header, "rideId", StringComparison.OrdinalIgnoreCase) || string.Equals(header, "MediRoutesClient", StringComparison.OrdinalIgnoreCase)) 
            { 
                return true;
            }
            else
            {
                return false;
            }

        }

        public bool IsRide2mdCorrectFormat()
        {
            // Define CsvHelper settings
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = (args) =>
                {
                    Console.WriteLine($"Missing field found. Headers: '{string.Join(", ", args.HeaderNames ?? new string[0])}', Index: {args.Index}, Row: {args.Context.Parser.Row}");
                },
            };

            using var reader = new StreamReader(_csvFilePath);
            using var csv = new CsvHelper.CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var actualCsvHeaders = csv.Context.Reader.HeaderRecord;
            if (actualCsvHeaders == null)
            {
                throw new InvalidOperationException("The CSV file contains no headers or is empty.");
            }
            var header = actualCsvHeaders[47];
            if (string.Equals(header, "AddressDO", StringComparison.OrdinalIgnoreCase) )
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        public List<CsvTripRawModel> ReadCsv()
        {
            using var reader = new StreamReader(_csvFilePath);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            });

            return csv.GetRecords<CsvTripRawModel>().ToList();
        }

        public List<CsvTripRawModel> ReadCsvWithDuplicateColumns(string jsonFileName)
        {
            // Read and process headers
            string[] originalHeaders;
            Dictionary<string, int> headerIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> headerMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var reader = new StreamReader(_csvFilePath))
            using (var csv = new CsvHelper.CsvReader(reader, System.Globalization.CultureInfo.InvariantCulture))
            {
                csv.Read();
                csv.ReadHeader();
                originalHeaders = csv.Context.Reader.HeaderRecord ?? throw new InvalidOperationException("The CSV file contains no headers or is empty.");

                // Process duplicate headers
                var headerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < originalHeaders.Length; i++)
                {
                    var originalHeader = originalHeaders[i];
                    string mappedHeader = originalHeader;

                    if (headerCounts.ContainsKey(originalHeader))
                    {
                        headerCounts[originalHeader]++;
                        mappedHeader = originalHeader + "DO";
                    }
                    else
                    {
                        headerCounts[originalHeader] = 1;
                    }

                    headerIndices[mappedHeader] = i;                   
                    headerMappings[mappedHeader] = mappedHeader;
                }
            }

            // Load JSON mapping
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string mappingPath = Path.Combine(baseDirectory, "Assets", "Mappings", jsonFileName);

            if (!File.Exists(mappingPath))
            {
                throw new FileNotFoundException($"The mapping file was not found: {mappingPath}");
            }

            var mappingJson = File.ReadAllText(mappingPath);
            var tempMapping = JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingJson)
                ?? throw new Exception($"Error deserializing JSON mapping file: {jsonFileName}.");

            // Combine the mappings
            var csvToPropertyMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in tempMapping)
            {
                if (headerMappings.TryGetValue(mapping.Key, out var mappedHeader))
                {
                    csvToPropertyMappings[mappedHeader] = mapping.Value;
                }
            }

            // CsvHelper Configuration
            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null 
            };

            // Read the data
            using (var reader = new StreamReader(_csvFilePath))
            using (var csv = new CsvHelper.CsvReader(reader, config))
            {
                var map = new CsvTripRawModelMap(headerIndices, csvToPropertyMappings);
                csv.Context.RegisterClassMap(map);

                return csv.GetRecords<CsvTripRawModel>().ToList();
            }
        }

        // If ReadCsv may take a long time, consider async Task<List<CsvTripRawModel>>
        // and run it with Task.Run().
        public List<CsvTripRawModel> ReadCsvWithoutDuplicateColumns(string jsonFileName)
        {

            // Define CsvHelper settings
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = (args) =>
                {
                    Console.WriteLine($"Missing field found. Headers: '{string.Join(", ", args.HeaderNames ?? new string[0])}', Index: {args.Index}, Row: {args.Context.Parser.Row}");
                },
            };

            using var reader = new StreamReader(_csvFilePath);
            using var csv = new CsvHelper.CsvReader(reader, config);

            csv.Read();
            csv.ReadHeader();
            var actualCsvHeaders = csv.Context.Reader.HeaderRecord;
            if (actualCsvHeaders == null)
            {
                throw new InvalidOperationException("The CSV file contains no headers or is empty.");
            }

            // Remove duplicate columns
            var uniqueHeaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var headerOriginalIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < actualCsvHeaders.Length; i++)
            {
                var header = actualCsvHeaders[i];
                if (!uniqueHeaderNames.Contains(header))
                {
                    uniqueHeaderNames.Add(header);
                    headerOriginalIndices[header] = i;
                }               
            }

            //string mappingFileName = "SAFERIDE.json";
            string mappingFileName = jsonFileName;
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string mappingPath = System.IO.Path.Combine(baseDirectory, "Assets", "Mappings", mappingFileName);

            if (!File.Exists(mappingPath))
            {
                throw new FileNotFoundException($"The mapping file was not found: {mappingPath}");
            }

            var mappingJson = File.ReadAllText(mappingPath);
            var tempMapping = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(mappingJson);

            if (tempMapping == null)
            {
                throw new Exception($"Error deserializing JSON mapping file: {mappingFileName}.");
            }
            var csvToPropertyMappings = new Dictionary<string, string>(tempMapping, StringComparer.OrdinalIgnoreCase);

            var map = new CsvTripRawModelMap(headerOriginalIndices, csvToPropertyMappings);
            csv.Context.RegisterClassMap(map);

            return csv.GetRecords<CsvTripRawModel>().ToList();
        }
       
    }

}
