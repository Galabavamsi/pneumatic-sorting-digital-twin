using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PlcGateway
{
    // Read-only gateway: PLC (EtherNet/IP) -> JSON for the dashboard app.
    //
    //   dotnet run                    read the real PLC, serve on http://localhost:8787
    //   dotnet run -- --demo          no PLC needed: plays a simulated Kit 3 cycle
    //   dotnet run -- --lan           also reachable from other devices on this network
    //   dotnet run -- --port 9000     another port
    //   dotnet run -- --plc 169.254.177.240   skip discovery
    //
    //   GET /api/state     latest snapshot
    //   GET /api/stream    the same snapshot, pushed as Server-Sent Events when it changes
    //   GET /api/health    "ok"
    //
    // There is no endpoint that writes to the controller, and the EtherNet/IP
    // client used here only implements "Read Tag".
    public static class Program
    {
        public static void Main(string[] args)
        {
            bool demo = args.Contains("--demo");
            bool lan = args.Contains("--lan");
            int port = int.Parse(ArgValue(args, "--port") ?? "8787");
            string plcHost = ArgValue(args, "--plc") ?? "auto";

            List<JsonObject> profiles = LoadProfiles();
            StateHub hub = new StateHub();
            Poller poller = new Poller(hub, profiles, plcHost, demo);
            Thread thread = new Thread(poller.Run) { IsBackground = true, Name = "plc-poller" };
            thread.Start();

            CloudPublisher cloud = CloudPublisher.TryCreate(hub);
            if (cloud != null)
            {
                new Thread(cloud.Run) { IsBackground = true, Name = "cloud-publisher" }.Start();
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                WebRootPath = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
                    ? Path.Combine(AppContext.BaseDirectory, "wwwroot")
                    : null
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.UseUrls($"http://{(lan ? "0.0.0.0" : "localhost")}:{port}");
            WebApplication app = builder.Build();

            // Read-only data, so any origin may fetch it (the app may be hosted elsewhere).
            app.Use(async (context, next) =>
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                context.Response.Headers["Cache-Control"] = "no-store";
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 204;
                    return;
                }

                if (context.Request.Method != "GET" && context.Request.Method != "HEAD")
                {
                    context.Response.StatusCode = 405;   // nothing here accepts writes
                    return;
                }

                await next();
            });

            app.MapGet("/api/health", () => "ok");

            app.MapGet("/api/state", (HttpContext context) =>
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                return context.Response.WriteAsync(hub.Json);
            });

            app.MapGet("/api/stream", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                long sent = -1;
                DateTime lastWrite = DateTime.UtcNow;
                CancellationToken stop = context.RequestAborted;
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        long version = hub.Version;
                        if (version != sent)
                        {
                            sent = version;
                            await context.Response.WriteAsync("data: " + hub.Json + "\n\n", stop);
                            await context.Response.Body.FlushAsync(stop);
                            lastWrite = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - lastWrite).TotalSeconds > 15)
                        {
                            await context.Response.WriteAsync(": keep-alive\n\n", stop);
                            await context.Response.Body.FlushAsync(stop);
                            lastWrite = DateTime.UtcNow;
                        }

                        await Task.Delay(50, stop);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            });

            if (app.Environment.WebRootPath != null)
            {
                app.UseDefaultFiles();
                Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider types =
                    new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
                types.Mappings[".glb"] = "model/gltf-binary";     // the 3D model
                app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = types });
                app.MapFallbackToFile("index.html");
            }

            Console.WriteLine($"PLC gateway ({(demo ? "DEMO data" : "live PLC, read-only")}) on http://localhost:{port}" +
                (lan ? "  (and this PC's network address)" : ""));
            Console.WriteLine(cloud != null ? "Cloud relay: publishing to " + cloud.Describe() : "Cloud relay: off (no cloud.json)");
            app.Run();
        }

        private static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static List<JsonObject> LoadProfiles()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "profiles");
            List<JsonObject> list = new List<JsonObject>();
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderByDescending(f => f))
            {
                list.Add(JsonNode.Parse(File.ReadAllText(file)).AsObject());
            }

            if (list.Count == 0)
            {
                throw new InvalidOperationException("No kit profiles found in " + dir);
            }

            return list;
        }
    }

    // Latest snapshot, shared between the poller thread and the web requests.
    public sealed class StateHub
    {
        private volatile string json = "{\"link\":{\"connected\":false,\"error\":\"Starting\"}}";
        private long version;
        private long coreVersion;

        public string Json => json;
        public long Version => Interlocked.Read(ref version);

        public long CoreVersion => Interlocked.Read(ref coreVersion);

        // coreChanged: something other than the timestamp changed.
        public void Publish(string value, bool coreChanged)
        {
            json = value;
            Interlocked.Increment(ref version);
            if (coreChanged)
            {
                Interlocked.Increment(ref coreVersion);
            }
        }
    }
}
