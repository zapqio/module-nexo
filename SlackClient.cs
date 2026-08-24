using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Zapqio.Runner.Core;

namespace Nexo
{
    /// <summary>
    /// Prosty klient Slacka - wysyla powiadomienie tekstowe przez chat.postMessage.
    /// Token bota (xoxb-...) i domyslny kanal siedza w nexoModule.json: SlackToken, SlackChannel.
    ///
    /// Wysylka nigdy nie wywraca operacji biznesowej - blad ladzie w logu, metoda zwraca false.
    /// </summary>
    public class SlackClient : IRunnerInjection, IDisposable
    {
        private const string PostMessageUrl = "https://slack.com/api/chat.postMessage";

        private readonly Settings _settings;
        private HttpClient _http;

        public SlackClient(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>Wysyla wiadomosc na kanal z ustawien (SlackChannel).</summary>
        public Task<bool> Send(string message)
        {
            return Send(message, null);
        }

        /// <summary>
        /// Wysyla wiadomosc na wskazany kanal (np. "#erp-alerty" albo ID kanalu).
        /// Tekst idzie jako mrkdwn, wiec dziala *pogrubienie*, `kod` itd.
        /// </summary>
        public async Task<bool> Send(string message, string channel)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                // Bez tego Slack odrzucilby wywolanie bledem "no_text" - lepiej nie wolac go w ogole.
                Console.WriteLine("Slack: pusta treść wiadomości, pomijam powiadomienie");
                return false;
            }

            var target = !string.IsNullOrWhiteSpace(channel) ? channel : _settings?.SlackChannel;
            var token = _settings?.SlackToken;

            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(target))
            {
                Console.Error.WriteLine("Slack: brak konfiguracji (SlackToken / SlackChannel), pomijam powiadomienie");
                return false;
            }

            try
            {
                var payload = JsonSerializer.Serialize(new Message { Channel = target, Text = message });
                using var request = new HttpRequestMessage(HttpMethod.Post, PostMessageUrl)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var response = await Http().SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                // chat.postMessage prawie zawsze zwraca 200 - o wyniku decyduje pole "ok" w tresci.
                var result = JsonSerializer.Deserialize<Response>(body);
                if (!response.IsSuccessStatusCode || result == null || !result.Ok)
                {
                    Console.Error.WriteLine($"Slack: nie wysłano powiadomienia na {target} - {result?.Error ?? body}");
                    return false;
                }

                Console.WriteLine($"Slack: wysłano powiadomienie na {target}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Slack: nie udało się wysłać powiadomienia: {ex.Message}");
                return false;
            }
        }

        private HttpClient Http()
        {
            return _http ??= new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        }

        public void Dispose()
        {
            _http?.Dispose();
            _http = null;
        }

        private class Message
        {
            [JsonPropertyName("channel")]
            public string Channel { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        private class Response
        {
            [JsonPropertyName("ok")]
            public bool Ok { get; set; }

            [JsonPropertyName("error")]
            public string Error { get; set; }
        }
    }
}
