using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Views;
using System.Windows;

namespace Raphael.Desktop.Services
{
    public static class ApiClientFactory
    {
        private static readonly string _baseUrl = App.Configuration["ApiAddress:ApiTest"];  // App.Configuration["ApiAddress:ApiTest"]; // App.Configuration["ApiAddress:ApiService"];// App.Configuration["ApiAddress:GatewayService"];
        private static readonly string _prefix = "api/";
        private static readonly string URI = _baseUrl + _prefix;
        public static HttpClient Create()
        {
            //System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            var client = new HttpClient
            {                 
                BaseAddress = new Uri(URI)
            };

            if (!string.IsNullOrEmpty(SessionManager.Token))
            {
                if (JwtHelper.IsTokenExpired(SessionManager.Token))
                {
                    MessageBox.Show("Your session has expired. Please log in again.", "Session expired", MessageBoxButton.OK, MessageBoxImage.Warning);

                    SessionManager.Clear();

                    // Open login window
                    var login = new LoginWindow();
                    login.Show();

                    foreach (Window window in Application.Current.Windows)
                    {
                        // If it is not the login window, it closes
                        if (window is not LoginWindow)
                        {
                            window.Close();
                        }
                    }

                    // Close all windows except the main one (if necessary)
                    /*foreach (Window window in Application.Current.Windows)
                    {
                        if (window != Application.Current.MainWindow)
                        {
                            window.Close();
                        }
                    }

                    // Close the main window (in case we are in MainWindow or another)
                    Application.Current.MainWindow?.Close();
                                     
                
                    Application.Current.MainWindow = login;*/

                    return client;
                }

                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", SessionManager.Token);
            }

            return client;
        }
    }
}
