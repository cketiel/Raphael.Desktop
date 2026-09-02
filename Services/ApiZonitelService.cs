using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class ApiZonitelService
    {
        private readonly HttpClient _httpClient;
        private readonly ZonitelSettings _settings;

        // Cambiamos el constructor para recibir el IConfiguration completo
        public ApiZonitelService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            // Extraer la sección específica del IConfiguration
            _settings = configuration.GetSection("ApiZonitel").Get<ZonitelSettings>();

            if (_settings == null)
                throw new Exception("Could not find 'ApiZonitel' section in appsettings.json");

            _httpClient.BaseAddress = new Uri(_settings.BaseUrl);
            _httpClient.DefaultRequestHeaders.Add("X-Client-Id", _settings.ClientId);
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.UserPrivateToken);
        }

        public async Task<bool> SendSMSMessageRideHasBeenScheduled(
            string clientPhone,
            string date,
            string passengerName,
            string tripNumber,
            string pickupAddress,
            string pickupTime)
        {
            try
            {
                string endpoint = $"api/v{_settings.Version}/integrations/sms/send";

                string messageText = "Ride scheduled.\n" +
                                   $"Date: {date}\n" +
                                   $"Name: {passengerName}\n" +
                                   //$"Trip Number: {tripNumber}\n" +
                                   ///$"Address: {pickupAddress}\n" +
                                   $"Time: {pickupTime}\n" +
                                   "Please reply CONFIRM or DECLINE. Without confirmation, pickup cannot be guaranteed.\n" +
                                   "Visit www.etamilanes.com";

                // Espacios en blanco en exceso: Al usar el formato $@"" con sangría dentro del código C#, todos esos espacios de la izquierda se incluyen en el texto del mensaje, haciendo que supere el límite de caracteres y se vea mal.
                // Este no funcionaba pq tenia muchos espacios en blanco y la api solo soporta 320 caracteres.
                string messageTextOld = $@"Ride scheduled.
                                    Date: {date}
                                    Passenger Name: {passengerName}  
                                    Trip Number: {tripNumber}  
                                    Address: {pickupAddress}  
                                    Time: {pickupTime}
                                    visit www.etamilanes.com";

                var smsBody = new
                {
                    from = $"+1{_settings.MilanesTransportPhone}",
                    to = $"+1{clientPhone}",
                    text = messageText
                };

                // Escapado de caracteres Unicode: Por defecto, System.Text.Json convierte el símbolo + en \u002B. Aunque es JSON válido, muchas APIs no lo reconocen correctamente. (Zonitel no lo reconoce)
                // CONFIGURACIÓN CRUCIAL: Evitar que escape el símbolo '+'
                var options = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                };


                string jsonPayload = JsonSerializer.Serialize(smsBody, options);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, content);

                // Para debug: ver qué respondió la API si falla
                if (!response.IsSuccessStatusCode)
                {
                    string errorDetail = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Error Zonitel: {errorDetail}");
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Excepción: {ex.Message}");
                return false;
            }
        }
    }

    // Clase auxiliar para mapear el JSON
    public class ZonitelSettings
    {
        public string BaseUrl { get; set; }
        public int Version { get; set; }
        public string UserPrivateToken { get; set; }
        public string ClientId { get; set; }
        public string MilanesTransportPhone { get; set; }
    }
}