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

        /// <summary>
        /// One handler for the whole application, and therefore one connection pool.
        ///
        /// Every service in the app calls <see cref="Create"/> in its constructor, so a single
        /// open Schedule tab used to stand up eight or more HttpClients, each opening its own
        /// connections to a server that is on the internet and never releasing them. Sharing
        /// the handler keeps the TLS handshakes down to the ones actually needed.
        ///
        /// The clients on top of it stay per-service on purpose: each one bakes in the bearer
        /// token that was current when it was built, and that is the behaviour the services
        /// already rely on.
        ///
        /// PooledConnectionLifetime is what stops a long-lived pool from pinning a stale DNS
        /// answer — the reason a static HttpClient is otherwise a bad idea.
        /// </summary>
        private static readonly SocketsHttpHandler SharedHandler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 20,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };

        public static HttpClient Create()
        {
            //System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            var client = new HttpClient(SharedHandler, disposeHandler: false)
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
