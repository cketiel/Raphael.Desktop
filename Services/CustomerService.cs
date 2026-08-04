using Raphael.Desktop.Models;

using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System;
using System.Windows;
using System.Windows.Xps;
using Raphael.Desktop.Exceptions;
using Newtonsoft.Json.Linq;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Raphael.Desktop.Services
{
    public class CustomerService : ICustomerService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "customers";
        //private string URI = App.Configuration["ApiAddress:TripsService"];

        public CustomerService()
        {
            _httpClient = ApiClientFactory.Create(); 
            //_httpClient = new HttpClient();
            //_httpClient.BaseAddress = new Uri(URI);
        }

        public async Task<List<Customer>> GetAllCustomersAsync()
        {
            //await Task.Delay(500); // Simulates latency
            try
            {

                var response = await _httpClient.GetAsync(EndPoint);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error getting clients");
                }

                return await response.Content.ReadFromJsonAsync<List<Customer>>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
        }
     
        public async Task<Customer> GetCustomerByIdAsync(int id)
        {
            try
            {               
                var response = await _httpClient.GetAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error getting client {id}");
                }

                return await response.Content.ReadFromJsonAsync<Customer>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            //return await _httpClient.GetFromJsonAsync<Customer>($"{EndPoint}/{id}");
        }

        public async Task<Customer> CreateCustomerAsync(CustomerCreateDto customer)
        {
            //Customer result = new Customer();
            if (customer.Latitude == null || customer.Longitude == null) 
            {
                GoogleMapsService googleMapsService = new GoogleMapsService();
                Coordinates coords = await googleMapsService.GetCoordinates(customer.Address, customer.City, customer.State, customer.Zip);
                customer.Latitude = coords.Latitude;
                customer.Longitude = coords.Longitude;
            }

            try
            {

                var response = await _httpClient.PostAsJsonAsync(EndPoint, customer);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error creating client");
                }

                return await response.Content.ReadFromJsonAsync<Customer>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }            
            /*catch (Exception ex)
            {
                // lanzar exception general;
            }*/
            //return result;
        }

        public async Task<bool> UpdateCustomerAsync(int id, CustomerCreateDto customer)
        {
            if (customer.Latitude == null || customer.Longitude == null)
            {
                GoogleMapsService googleMapsService = new GoogleMapsService();
                Coordinates coords = await googleMapsService.GetCoordinates(customer.Address, customer.City, customer.State, customer.Zip);
                customer.Latitude = coords.Latitude;
                customer.Longitude = coords.Longitude;
            }

            try
            {                                
                var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", customer);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error updating client {id}");
                }

                return true;
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            
            /*catch (Exception ex)
            {
                // Error general
            }*/

            //var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{customer.Id}", customer);
            //return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteCustomerAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting client {id}");
                }

                return true;
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            /*var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            return response.IsSuccessStatusCode;*/
        }

        public async Task<Customer> GetCustomerByRiderIdAsync(string riderId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{EndPoint}/rider/{riderId}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error getting Customer {riderId}");
                }

                return await response.Content.ReadFromJsonAsync<Customer>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }           
        }

        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                return new ApiException(
                    message: $"{context}: {problemDetails?.Title}",
                    statusCode: response.StatusCode,
                    details: problemDetails?.Detail);
            }
            catch
            {
                var content = await response.Content.ReadAsStringAsync();
                return new ApiException(
                    message: $"{context}: Unspecified error",
                    statusCode: response.StatusCode,
                    details: content);
            }
        }

       
    }
}
