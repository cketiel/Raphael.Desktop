
using DocumentFormat.OpenXml.Vml.Spreadsheet;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class AuthService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUri;

        public AuthService()
        {
            _httpClient = new HttpClient();
            //_baseUri = "API" ?? throw new ApiException("API address configuration is missing.");
            _baseUri = App.Configuration["ApiAddress:ApiTest"] ?? throw new ApiException("API address configuration is missing.");

            try
            {
                _httpClient.BaseAddress = new Uri(_baseUri);
                _httpClient.Timeout = TimeSpan.FromSeconds(9000); // Set reasonable timeout //30
            }
            catch (UriFormatException ex)
            {
                throw new ApiException($"Invalid API address format: {_baseUri}", ex);
            }
            catch (Exception ex)
            {
                throw new ApiException("Failed to initialize authentication service.", ex);
            }
        }

        public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            // Validate input parameters
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = "Username and password are required."
                };
            }

            var request = new LoginRequest
            {
                Username = username,
                Password = password
            };
           // var showRealResponse = new object();
            var showRealResponse = "Real response";
            try
            {
                System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;

                using var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);

                if (!response.IsSuccessStatusCode)
                {
                    // Handle specific HTTP status codes
                    return response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => new LoginResponse
                        {
                            IsSuccess = false,
                            Message = "Invalid credentials. Please check your username and password."
                        },
                        HttpStatusCode.NotFound => new LoginResponse
                        {
                            IsSuccess = false,
                            Message = "Authentication service not found."
                        },
                        HttpStatusCode.ServiceUnavailable => new LoginResponse
                        {
                            IsSuccess = false,
                            Message = "Service temporarily unavailable. Please try again later."
                        },
                        HttpStatusCode.Forbidden => new LoginResponse
                        {
                            IsSuccess = false,
                            Message = "User account is disabled. Contact administrator."
                        },
                        _ => new LoginResponse
                        {
                            IsSuccess = false,
                            Message = $"Authentication failed. Status: {response.StatusCode}"
                        }
                    };
                }

                showRealResponse = await response.Content.ReadAsStringAsync() + "---" + response.StatusCode;
                response.EnsureSuccessStatusCode();
                var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();

                return loginResponse ?? new LoginResponse
                {
                    IsSuccess = false,
                    Message = "Received empty response from server."
                };
            }
            catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException)
            {
                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = "Network error. Please check your internet connection."
                };
            }
            catch (HttpRequestException ex)
            {
                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = $"Server communication error: {ex.Message}"
                };
            }
            catch (JsonException ex)
            {
                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = $" Real Response: {showRealResponse} -InnerException: {ex.InnerException} "
                    //Message = $"Error processing server response. -> {ex.Message} InnerException: {ex.InnerException} "
                };
            }
            catch (TaskCanceledException)
            {
                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = "Request timeout. Please try again."
                };
            }
            catch (Exception ex)
            {
                // Log this unexpected error for debugging
                System.Diagnostics.Debug.WriteLine($"Unexpected error during login: {ex}");

                return new LoginResponse
                {
                    IsSuccess = false,
                    Message = "An unexpected error occurred during authentication."
                };
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}



/*

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private string URI = App.Configuration["ApiAddress:ApiTest"];
        //private string URI = App.Configuration["ApiAddress:ApiService"];
        //private string URI = App.Configuration["ApiAddress:UsersService"];
        public AuthService()
        {
            _httpClient = new HttpClient();
            try
            {
                _httpClient.BaseAddress = new System.Uri("URI");
            }
            // Format error in the URI configuration in the appsettings.json configuration file
            catch (UriFormatException ex) 
            {
                throw new ApiException("Format error in the URI configuration in the appsettings.json configuration file.", ex);
            }
            catch (Exception ex)
            {

                throw new ApiException("An unexpected error occurred during URI configuration.", ex);
            }
            
        }

        public async Task<LoginResponse> LoginAsync(string username, string password)
        {
            var request = new LoginRequest
            {
                Username = username,
                Password = password
            };

            if (_httpClient.BaseAddress == null)
            {
                // If BaseAddress could not be set in the constructor.
                throw new ApiException("The authentication service configuration is incorrect (invalid base URL).");
            }
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);

                if (!response.IsSuccessStatusCode)
                {
                    //throw await CreateApiException(response, "Authentication error");
                    //throw await CreateApiException(response, $"Authentication error ({(int)response.StatusCode})");
                    return new LoginResponse { IsSuccess = false, Message = "Login Failed: Invalid credentials. Incorrect username or password." };
                }
                // Trying to deserialize, could fail if the JSON is not as expected.
                var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
                if (loginResponse == null)
                {
                    throw new ApiException("Unexpected response from the server after login.");
                }
                return loginResponse;

                //return await response.Content.ReadFromJsonAsync<LoginResponse>();               
            }
            catch (HttpRequestException ex) // Network errors, DNS, server not available, etc.
            {
                throw new ApiException("Server connection error. Check your internet connection.", ex);
            }
            catch (JsonException ex) // Error deserializing JSON response
            {
                throw new ApiException("Error processing server response.", ex);
            }

            catch (Exception ex)
            {
                throw new ApiException("An unexpected error occurred during login.", ex);
            }

            
        }
        

    }
}

*/


/*var request = new LoginRequest
            {
                Username = username,
                Password = password
            };

            var response = await _httpClient.PostAsJsonAsync("api/auth/login", request);
            
            if (response.IsSuccessStatusCode)
            {
                
                return await response.Content.ReadFromJsonAsync<LoginResponse>();
            }

            return new LoginResponse { IsSuccess = false, Message = "Invalid login" };*/