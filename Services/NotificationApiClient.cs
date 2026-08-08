using Newtonsoft.Json;
using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class NotificationApiClient
    {
        private readonly HttpClient _httpClient;

        public NotificationApiClient()
        {
            _httpClient = ApiClientFactory.Create();
        }

        /// <summary>
        /// Gets all notifications for the currently authenticated user.
        /// </summary>
        public async Task<IReadOnlyList<NotificationDto>> GetNotificationsAsync(
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(
                "notifications",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);

            var notifications =
                JsonConvert.DeserializeObject<List<NotificationDto>>(json);

            return notifications ??
                   new List<NotificationDto>();
        }

        /// <summary>
        /// Gets a notification by its identifier.
        /// </summary>
        public async Task<NotificationDto?> GetNotificationByIdAsync(
            Guid notificationId,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(
                $"notifications/{notificationId}",
                cancellationToken);

            if (response.StatusCode ==
                System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);

            return JsonConvert.DeserializeObject<NotificationDto>(
                json);
        }

        /// <summary>
        /// Marks a notification recipient as viewed.
        /// </summary>
        public async Task MarkViewedAsync(
            Guid notificationRecipientId,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.PostAsync(
                $"notifications/{notificationRecipientId}/view",
                content: null,
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// Marks a notification recipient as acknowledged.
        /// </summary>
        public async Task MarkAcknowledgedAsync(
            Guid notificationRecipientId,
            CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.PostAsync(
                $"notifications/{notificationRecipientId}/acknowledge",
                content: null,
                cancellationToken);

            response.EnsureSuccessStatusCode();
        }
    }
}