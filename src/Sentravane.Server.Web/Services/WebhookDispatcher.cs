using System.Text;
using System.Text.Json;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Services;

public class WebhookDispatcher(IHttpClientFactory httpClientFactory, ILogger<WebhookDispatcher> logger)
{
    public async Task BroadcastIncidentAsync(string webhookUrl, ForensicSnapshotPayload snapshot)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;

        try
        {
            var client = httpClientFactory.CreateClient();
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(webhookUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webhook dispatch returned non-success code: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch incident webhook to {Url}", webhookUrl);
        }
    }
}

