using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace PlcGateway
{
    // Optional: pushes the latest snapshot to the cloud relay (Dashboard/cloud/schema.sql)
    // so the app can read it from anywhere. Outbound HTTPS only; nothing connects
    // in to this PC. Enabled by a cloud.json file next to the gateway:
    //
    //   { "url": "https://<project>.supabase.co", "key": "<publishable key>",
    //     "machineId": "rig-1", "token": "<ingest token>" }
    public sealed class CloudPublisher
    {
        private const int MinGapMs = 400;        // at most ~2.5 pushes a second while the machine moves
        private const int HeartbeatMs = 5000;    // and one every 5 s while nothing changes

        private readonly StateHub hub;
        private readonly string url;
        private readonly string key;
        private readonly string machineId;
        private readonly string token;
        private readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        private CloudPublisher(StateHub hub, JsonNode config)
        {
            this.hub = hub;
            url = ((string)config["url"]).TrimEnd('/');
            key = (string)config["key"];
            machineId = (string)config["machineId"] ?? "rig-1";
            token = (string)config["token"];
        }

        public static CloudPublisher TryCreate(StateHub hub)
        {
            foreach (string path in new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), "cloud.json"),
                Path.Combine(AppContext.BaseDirectory, "cloud.json")
            })
            {
                if (File.Exists(path))
                {
                    JsonNode config = JsonNode.Parse(File.ReadAllText(path));
                    if (config["url"] != null && config["key"] != null && config["token"] != null)
                    {
                        return new CloudPublisher(hub, config);
                    }
                }
            }

            return null;
        }

        public string Describe()
        {
            return $"{url} as '{machineId}'";
        }

        public void Run()
        {
            long sentCore = -1;
            DateTime lastPush = DateTime.MinValue;
            string lastError = null;
            while (true)
            {
                Thread.Sleep(100);
                double since = (DateTime.UtcNow - lastPush).TotalMilliseconds;
                long core = hub.CoreVersion;
                bool changed = core != sentCore && since >= MinGapMs;
                if (!changed && since < HeartbeatMs)
                {
                    continue;
                }

                try
                {
                    JsonObject body = new JsonObject
                    {
                        ["p_machine"] = machineId,
                        ["p_token"] = token,
                        ["p_snapshot"] = JsonNode.Parse(hub.Json)
                    };
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url + "/rest/v1/rpc/ingest_machine_state"))
                    {
                        request.Headers.TryAddWithoutValidation("apikey", key);
                        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                        using (HttpResponseMessage response = http.Send(request))
                        {
                            if (!response.IsSuccessStatusCode)
                            {
                                throw new Exception("cloud relay answered " + (int)response.StatusCode);
                            }
                        }
                    }

                    sentCore = core;
                    if (lastError != null)
                    {
                        Console.WriteLine("cloud relay: back online");
                        lastError = null;
                    }
                }
                catch (Exception e)
                {
                    if (e.Message != lastError)
                    {
                        Console.WriteLine("cloud relay: " + e.Message);
                        lastError = e.Message;
                    }
                }

                lastPush = DateTime.UtcNow;
            }
        }
    }
}
