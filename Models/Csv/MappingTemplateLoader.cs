using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Models.Csv
{
    public static class MappingTemplateLoader
    {
        public static CsvMappingTemplate LoadTemplate(string filePath)
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<CsvMappingTemplate>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
    }

}
