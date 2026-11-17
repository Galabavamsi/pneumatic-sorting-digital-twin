using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Kit1DigitalTwin.PlcLink;

namespace PlcGateway
{
    // Polls the controller, works out which kit program is running, and turns the
    // raw variable values into the snapshot the dashboard shows.
    public sealed class Poller
    {
        private const int PollMs = 120;
        private const int MaxEvents = 120;
        private const int MaxCycles = 30;

        private readonly StateHub hub;
        private readonly List<JsonObject> profiles;
        private readonly string plcHost;
        private readonly bool demo;
        private readonly DateTime startedAt = DateTime.UtcNow;
        private readonly DemoSource demoSource = new DemoSource();

        private Micro800EipClient client;
        private JsonObject profile;
        private List<string> variables;
        private string linkError = "Looking for the PLC";

        // Derived state that outlives one poll.
        private readonly LinkedList<JsonObject> events = new LinkedList<JsonObject>();
        private readonly Dictionary<string, string> heldPosition = new Dictionary<string, string>();
        private readonly List<double> cycleSeconds = new List<double>();
        private int? lastStep;
        private int? lastFault;
        private int? lastParts;
        private int lastMaterial;
        private DateTime stepSince = DateTime.UtcNow;
        private DateTime? cycleStart;
        private bool wasConnected;
        private string lastCore;
        private DateTime lastPublish = DateTime.MinValue;

        public Poller(StateHub hub, List<JsonObject> profiles, string plcHost, bool demo)
        {
            this.hub = hub;
            this.profiles = profiles;
            this.plcHost = plcHost;
            this.demo = demo;
        }

        public void Run()
        {
            while (true)
            {
                Stopwatch watch = Stopwatch.StartNew();
                Dictionary<string, int?> values = null;
                try
                {
                    values = demo ? ReadDemo() : ReadPlc();
                    linkError = null;
                }
                catch (Exception e)
                {
                    linkError = e.Message;
                    CloseClient();
                }

                try
                {
                    Publish(values, (int)watch.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    Console.WriteLine("snapshot error: " + e.Message);
                }

                int wait = values == null ? 2000 : Math.Max(10, PollMs - (int)watch.ElapsedMilliseconds);
                Thread.Sleep(wait);
            }
        }

        private Dictionary<string, int?> ReadDemo()
        {
            if (profile == null)
            {
                SelectProfile(profiles.First(p => (string)p["id"] == "kit3"));
            }

            return demoSource.Read(variables);
        }

        private Dictionary<string, int?> ReadPlc()
        {
            if (client == null || !client.Connected)
            {
                CloseClient();
                client = new Micro800EipClient(plcHost, 1500);
                client.Connect();
                profile = null;
            }

            if (profile == null)
            {
                foreach (JsonObject candidate in profiles)
                {
                    try
                    {
                        client.ReadInt((string)candidate["probe"]);
                        SelectProfile(candidate);
                        break;
                    }
                    catch (EipException)
                    {
                        // this kit's program is not the one in the controller
                    }
                }

                if (profile == null)
                {
                    throw new EipException("The PLC answered, but it is not running a Kit 1 or Kit 3 program.");
                }
            }

            Dictionary<string, int?> values = new Dictionary<string, int?>();
            string stepVar = (string)profile["probe"];
            try
            {
                values[stepVar] = client.ReadInt(stepVar);
            }
            catch (EipException)
            {
                profile = null;     // program changed (a download) or the link dropped: start again
                throw;
            }

            foreach (string name in variables)
            {
                if (name == stepVar)
                {
                    continue;
                }

                try
                {
                    values[name] = client.ReadInt(name);
                }
                catch (EipException)
                {
                    values[name] = null;    // variable not in this program
                    if (!client.Connected)
                    {
                        throw;
                    }
                }
            }

            return values;
        }

        private void SelectProfile(JsonObject selected)
        {
            profile = selected;
            HashSet<string> names = new HashSet<string>();
            foreach (KeyValuePair<string, JsonNode> v in profile["vars"].AsObject())
            {
                names.Add((string)v.Value);
            }

            names.Add((string)profile["probe"]);

            foreach (string section in new[] { "counters", "inputs", "outputs", "settings" })
            {
                foreach (JsonNode item in profile[section].AsArray())
                {
                    names.Add((string)item["var"]);
                }
            }

            foreach (JsonNode actuator in profile["actuators"].AsArray())
            {
                foreach (string side in new[] { "a", "b" })
                {
                    string name = (string)actuator[side]["var"];
                    if (name != null)
                    {
                        names.Add(name);
                    }
                }
            }

            variables = names.ToList();
            heldPosition.Clear();
            lastStep = null;
            lastFault = null;
            lastParts = null;
            cycleStart = null;
            cycleSeconds.Clear();
            AddEvent("info", $"Watching {(string)profile["name"]}: {(string)profile["title"]}");
        }

        private void CloseClient()
        {
            try
            {
                client?.Dispose();
            }
            catch
            {
            }

            client = null;
        }

        private void AddEvent(string kind, string text)
        {
            events.AddFirst(new JsonObject
            {
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["kind"] = kind,
                ["text"] = text
            });
            while (events.Count > MaxEvents)
            {
                events.RemoveLast();
            }
        }

        private static int? Val(Dictionary<string, int?> values, string name)
        {
            return name != null && values.TryGetValue(name, out int? v) ? v : null;
        }

        private static string Lookup(JsonNode table, int key, string fallback)
        {
            JsonNode found = table?[key.ToString()];
            return found != null ? (string)found : fallback;
        }

        private string StepName(int step)
        {
            foreach (JsonNode s in profile["steps"].AsArray())
            {
                if ((int)s["id"] == step)
                {
                    return (string)s["name"];
                }
            }

            return "Step " + step;
        }

        private void Publish(Dictionary<string, int?> values, int readMs)
        {
            bool connected = values != null;
            DateTime now = DateTime.UtcNow;
            if (connected != wasConnected)
            {
                AddEvent(connected ? "info" : "warn", connected ? "PLC link up" : "PLC link lost: " + linkError);
                wasConnected = connected;
            }

            JsonObject core = new JsonObject
            {
                ["gateway"] = new JsonObject
                {
                    ["version"] = "0.1.0",
                    ["mode"] = demo ? "demo" : "plc",
                    ["readOnly"] = true,
                    ["startedAt"] = startedAt.ToString("o")
                },
                ["link"] = new JsonObject
                {
                    ["connected"] = connected,
                    ["plc"] = demo ? "simulated" : client?.RemoteAddress,
                    ["product"] = demo ? "Demo data" : client?.ProductName,
                    ["error"] = linkError
                }
            };

            if (profile != null)
            {
                core["kit"] = new JsonObject
                {
                    ["id"] = (string)profile["id"],
                    ["name"] = (string)profile["name"],
                    ["title"] = (string)profile["title"],
                    ["description"] = (string)profile["description"],
                    ["steps"] = profile["steps"].DeepClone(),
                    ["sequence"] = profile["sequence"].DeepClone()
                };
            }

            if (connected)
            {
                AddMachineState(core, values, now);
            }

            core["events"] = new JsonArray(events.Select(e => e.DeepClone()).ToArray());

            // Publish on a real change, or once a second so "last update" stays fresh.
            string coreJson = core.ToJsonString();
            bool coreChanged = coreJson != lastCore;
            if (!coreChanged && (now - lastPublish).TotalMilliseconds < 1000)
            {
                return;
            }

            lastCore = coreJson;
            lastPublish = now;
            core["ts"] = now.ToString("o");
            ((JsonObject)core["link"])["readMs"] = readMs;
            if (connected)
            {
                int? heartbeat = Val(values, (string)profile["vars"]["heartbeat"]);
                ((JsonObject)core["status"])["heartbeat"] = heartbeat;
            }

            hub.Publish(core.ToJsonString(), coreChanged);
        }

        private void AddMachineState(JsonObject core, Dictionary<string, int?> values, DateTime now)
        {
            JsonNode vars = profile["vars"];
            int step = Val(values, (string)vars["step"]) ?? 0;
            int fault = Val(values, (string)vars["fault"]) ?? 0;
            int material = Val(values, (string)vars["material"]) ?? 0;
            int parts = Val(values, (string)vars["parts"]) ?? 0;
            bool running = (Val(values, (string)vars["running"]) ?? 0) != 0;
            bool faulted = (Val(values, (string)vars["faulted"]) ?? 0) != 0 || step == 90;

            if (material != 0)
            {
                lastMaterial = material;
            }

            if (lastStep != step)
            {
                if (lastStep.HasValue)
                {
                    AddEvent("step", $"{step} · {StepName(step)}");
                }

                if (step == (int)profile["cycleStartStep"])
                {
                    cycleStart = now;
                }

                lastStep = step;
                stepSince = now;
            }

            if (lastFault != fault)
            {
                if (fault != 0)
                {
                    AddEvent("fault", $"Fault {fault} · {Lookup(profile["faults"], fault, "Unknown fault")}");
                }
                else if (lastFault.HasValue)
                {
                    AddEvent("info", "Fault cleared");
                }

                lastFault = fault;
            }

            if (lastParts.HasValue && parts > lastParts.Value)
            {
                string what = Lookup(profile["materials"], lastMaterial, "part").ToLowerInvariant();
                if (cycleStart.HasValue)
                {
                    double seconds = Math.Round((now - cycleStart.Value).TotalSeconds, 1);
                    cycleSeconds.Add(seconds);
                    if (cycleSeconds.Count > MaxCycles)
                    {
                        cycleSeconds.RemoveAt(0);
                    }

                    AddEvent("part", $"Part {parts} sorted · {what} · {seconds:0.0} s");
                    cycleStart = null;
                }
                else
                {
                    AddEvent("part", $"Part {parts} sorted · {what}");
                }
            }
            else if (lastParts.HasValue && parts < lastParts.Value)
            {
                AddEvent("info", "Counters reset");
            }

            lastParts = parts;

            bool known = vars["step"] != null;
            string state = !known ? "unknown" : faulted ? "fault" : running ? "running" : step == 0 ? "idle" : "homing";
            core["status"] = new JsonObject
            {
                ["state"] = state,
                ["step"] = step,
                ["stepName"] = known ? StepName(step) : null,
                ["stepSince"] = stepSince.ToString("o"),
                ["faultCode"] = fault,
                ["faultText"] = fault != 0 ? Lookup(profile["faults"], fault, "Unknown fault") : null,
                ["material"] = material,
                ["materialName"] = Lookup(profile["materials"], material, "None")
            };

            core["cycle"] = new JsonObject
            {
                ["inProgressSince"] = cycleStart?.ToString("o"),
                ["lastSeconds"] = cycleSeconds.Count > 0 ? cycleSeconds[cycleSeconds.Count - 1] : (double?)null,
                ["averageSeconds"] = cycleSeconds.Count > 0 ? Math.Round(cycleSeconds.Average(), 1) : (double?)null,
                ["history"] = new JsonArray(cycleSeconds.Select(s => (JsonNode)JsonValue.Create(s)).ToArray())
            };

            JsonArray counters = new JsonArray();
            foreach (JsonNode c in profile["counters"].AsArray())
            {
                counters.Add(new JsonObject
                {
                    ["id"] = (string)c["id"],
                    ["label"] = (string)c["label"],
                    ["value"] = Val(values, (string)c["var"])
                });
            }

            core["counters"] = counters;

            foreach (string section in new[] { "inputs", "outputs" })
            {
                JsonArray list = new JsonArray();
                foreach (JsonNode io in profile[section].AsArray())
                {
                    int? v = Val(values, (string)io["var"]);
                    list.Add(new JsonObject
                    {
                        ["id"] = (string)io["id"],
                        ["label"] = (string)io["label"],
                        ["on"] = v.HasValue ? v.Value != 0 : (bool?)null
                    });
                }

                core[section] = list;
            }

            JsonArray actuators = new JsonArray();
            foreach (JsonNode a in profile["actuators"].AsArray())
            {
                string id = (string)a["id"];
                string aVar = (string)a["a"]["var"];
                string bVar = (string)a["b"]["var"];
                bool aOn = aVar != null && (Val(values, aVar) ?? 0) != 0;
                bool bOn = bVar != null && (Val(values, bVar) ?? 0) != 0;
                string position;
                bool driven = aOn || bOn;
                if (aOn)
                {
                    position = "a";
                }
                else if (bOn || bVar == null)
                {
                    position = "b";
                    driven = bOn;
                }
                else
                {
                    // Neither coil on: these valves stay where they were last put.
                    position = heldPosition.GetValueOrDefault(id);
                }

                if (position != null)
                {
                    heldPosition[id] = position;
                }

                actuators.Add(new JsonObject
                {
                    ["id"] = id,
                    ["label"] = (string)a["label"],
                    ["kind"] = (string)a["kind"],
                    ["position"] = position,
                    ["positionLabel"] = position == null ? "Unknown" : (string)a[position]["label"],
                    ["aLabel"] = (string)a["a"]["label"],
                    ["bLabel"] = (string)a["b"]["label"],
                    ["driven"] = driven
                });
            }

            core["actuators"] = actuators;

            JsonArray settings = new JsonArray();
            foreach (JsonNode s in profile["settings"].AsArray())
            {
                settings.Add(new JsonObject
                {
                    ["label"] = (string)s["label"],
                    ["unit"] = (string)s["unit"],
                    ["value"] = Val(values, (string)s["var"])
                });
            }

            core["settings"] = settings;
        }
    }
}
