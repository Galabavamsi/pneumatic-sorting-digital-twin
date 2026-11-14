using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Kit1DigitalTwin.PlcLink;

namespace Kit1PlcSimulator
{
    public static class Program
    {
        private const double ScanMs = 10;

        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "serve";
            switch (mode)
            {
                case "selftest":
                    return SelfTest();
                case "linktest":
                    return LinkTest();
                case "serve":
                    return Serve(args);
                case "plcread":
                    return PlcRead(args);
                default:
                    Console.WriteLine(
                        "Usage: Kit1PlcSimulator [serve [--port N] [--bind IP] | selftest | linktest | plcread [host]]");
                    return 2;
            }
        }

        // ------------------------------------------------------------------
        // plcread: read the real Micro850 over EtherNet/IP with the Unity reader
        // code. Read-only. Host defaults to "auto" (find the controller).
        // ------------------------------------------------------------------
        private static int PlcRead(string[] args)
        {
            string host = args.Length > 1 ? args[1] : Micro800EipClient.AutoHost;
            using (Kit1EipReader reader = new Kit1EipReader(host, 2000))
            {
                try
                {
                    reader.Connect();
                    reader.Read(out bool[] coils, out ushort[] registers);
                    Kit1PlcSnapshot snapshot = Kit1PlcSnapshot.Decode(coils, registers, DateTime.UtcNow);
                    Console.WriteLine($"Controller: {reader.Endpoint}");
                    Console.WriteLine($"Map version {snapshot.MapVersion}, heartbeat {snapshot.Heartbeat}");
                    Console.WriteLine($"Step {snapshot.Step}, fault {snapshot.FaultCode}, material {snapshot.Material}");
                    Console.WriteLine($"Parts {snapshot.PartsCompleted} (metal {snapshot.MetalCount}, " +
                        $"plastic {snapshot.PlasticCount}), last cycle {snapshot.LastCycleTimeMs} ms");
                    for (int index = 0; index < coils.Length; index++)
                    {
                        Console.WriteLine($"  {Kit1ModbusMap.CoilVariables[index],-24} {coils[index]}");
                    }

                    const int Polls = 100;
                    Stopwatch timer = Stopwatch.StartNew();
                    for (int poll = 0; poll < Polls; poll++)
                    {
                        reader.Read(out coils, out registers);
                    }

                    double perPoll = timer.Elapsed.TotalMilliseconds / Polls;
                    Console.WriteLine($"{Polls} full polls: {perPoll:0.0} ms each ({1000.0 / perPoll:0} Hz possible)");
                    bool versionOk = snapshot.MapVersion == Kit1ModbusMap.MapVersion;
                    Console.WriteLine(versionOk ? "PLCREAD PASS" : "PLCREAD FAIL (map version)");
                    return versionOk ? 0 : 1;
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"PLCREAD FAIL: {exception.GetType().Name}: {exception.Message}");
                    return 1;
                }
            }
        }

        // ------------------------------------------------------------------
        // selftest: deterministic scenarios in simulated time.
        // ------------------------------------------------------------------
        private static int failures;

        private static int SelfTest()
        {
            Scenario("Reed mode, fibre sensor, continuous batch of 12", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                List<Material> parts = RandomParts(12, seed: 7);
                rig.Plant.Load(parts);
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone > 0, 120000);
                CheckBatch(rig, parts);
            });

            Scenario("Metal routed to the upper tray when CFG_MetalToUpper = TRUE", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plc.CFG_MetalToUpper = true;
                List<Material> parts = RandomParts(6, seed: 3);
                rig.Plant.Load(parts);
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone > 0, 90000);
                Expect(rig.Plant.UpperTray.All(m => m == Material.Metal), "upper tray holds only metal");
                Expect(rig.Plant.LowerTray.All(m => m == Material.Plastic), "lower tray holds only plastic");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("Lift sensors fitted (CFG_LiftSensors = TRUE): lift moves wait for the sensors", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.LiftSensorsFitted = true;
                rig.Plc.CFG_LiftSensors = true;
                List<Material> parts = RandomParts(6, seed: 13);
                rig.Plant.Load(parts);
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone > 0, 90000);
                CheckBatch(rig, parts);
            });

            Scenario("No lift sensors, slow lift: timed moves sort the batch and Idle still reports homed", () =>
            {
                Kit1Rig rig = new Kit1Rig(travelMs: 900);   // slower than the other tests, still under the 1 s CFG_TravelTime
                rig.Plant.Load(new[] { Material.Metal, Material.Plastic, Material.Metal });
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == 3, 120000);
                Expect(rig.Plc.HR_FaultCode == 0, $"no fault (got {rig.Plc.HR_FaultCode})");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
                Run(rig, 5000);
                Expect(rig.Plc.HR_Step == 0 && rig.Plc.ST_Homed, "still reports homed after 5 s in Idle");
            });

            Scenario("No reeds, no fibre sensor: timed moves and inferred magazine empty", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.ReedsFitted = false;
                rig.Plc.CFG_UseReeds = false;
                rig.Plc.CFG_UseMagSensor = false;
                rig.Plc.CFG_TravelTime = 700;    // longer than the 400 ms plant travel
                List<Material> parts = RandomParts(5, seed: 11);
                rig.Plant.Load(parts);
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone > 0, 120000);
                CheckBatch(rig, parts);
            });

            Scenario("Single-cycle mode sorts exactly one part per Start", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.ContinuousSelector = false;
                rig.Plant.Load(RandomParts(4, seed: 5));
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == 1, 30000);
                Run(rig, 3000);
                Expect(rig.Plc.HR_PartsDone == 1, "one part after one Start");
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == 2, 30000);
                Expect(rig.Plant.Magazine.Count == 2, "two parts left in the magazine");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("Stop finishes the current part, homes, and goes Idle", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.Load(RandomParts(10, seed: 9));
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 5, 30000);
                rig.Plant.PressStop();
                RunUntil(rig, r => r.Plc.HR_Step == 0, 30000);
                Expect(rig.Plc.HR_PartsDone == 1, $"exactly one part finished (got {rig.Plc.HR_PartsDone})");
                Expect(rig.Plant.OnPlatform == null, "platform left empty");
                Expect(rig.Plc.ST_Homed, "machine homed");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("Stuck feed cylinder raises fault 2; Reset re-homes", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.Load(RandomParts(3, seed: 1));
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 3, 30000);
                rig.Plant.Feed.Stuck = true;
                RunUntil(rig, r => r.Plc.HR_Step == 90, 10000);
                Expect(rig.Plc.HR_FaultCode == 2, $"fault code 2 (got {rig.Plc.HR_FaultCode})");
                Expect(!rig.Plc.DO.Any(o => o), "all outputs off in fault");
                rig.Plant.Feed.Stuck = false;
                rig.Plant.PressReset();
                RunUntil(rig, r => r.Plc.HR_Step == 0, 30000);
                Expect(rig.Plc.HR_FaultCode == 0 && rig.Plc.ST_Homed, "fault cleared and homed");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("E-stop mid-cycle: fault 1, outputs off, Reset blocked until released", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.Load(RandomParts(5, seed: 2));
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 8 || r.Plc.HR_Step == 6, 30000);
                rig.Plant.EStopPressed = true;
                Run(rig, 50);
                Expect(rig.Plc.HR_Step == 90 && rig.Plc.HR_FaultCode == 1, "E-stop fault");
                Expect(!rig.Plc.DO.Any(o => o), "all outputs off");
                rig.Plant.PressReset();
                Run(rig, 1000);
                Expect(rig.Plc.HR_Step == 90, "Reset ignored while E-stop pressed");
                rig.Plant.EStopPressed = false;
                rig.Plant.PressReset();
                RunUntil(rig, r => r.Plc.HR_Step == 0, 30000);
                Expect(rig.Plc.ST_Homed, "homed after reset");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("Fibre sensor fitted but part never reaches IS/CS: fault 4", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.Load(new[] { Material.Metal });
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 4, 30000);
                // Knock the part off the platform before detection.
                typeof(Kit1Plant).GetField("onPlatform",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .SetValue(rig.Plant, null);
                RunUntil(rig, r => r.Plc.HR_Step == 90, 10000);
                Expect(rig.Plc.HR_FaultCode == 4, $"fault code 4 (got {rig.Plc.HR_FaultCode})");
            });

            Scenario("Stop pressed during start-up homing cancels the run (no feed)", () =>
            {
                Kit1Rig rig = new Kit1Rig();      // lift starts down, so homing takes a while
                rig.Plant.Load(RandomParts(4, seed: 4));
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 1, 5000);
                Run(rig, 300);
                Expect(rig.Plc.HR_Step == 1, "still homing when Stop is pressed");
                rig.Plant.PressStop();
                double maxFeed = 0;
                for (double t = 0; t < 15000; t += ScanMs)
                {
                    rig.Step(ScanMs);
                    maxFeed = Math.Max(maxFeed, rig.Plant.Feed.Position);
                }

                Expect(rig.Plc.HR_Step == 0, $"ends Idle (step {rig.Plc.HR_Step})");
                Expect(maxFeed < 0.01, $"feeder never moved (max position {maxFeed:0.00})");
                Expect(rig.Plc.HR_PartsDone == 0 && rig.Plant.Magazine.Count == 4, "no part taken from the magazine");
                Expect(rig.Plc.ST_Homed, "machine homed");
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 3, 15000);   // a later Start still works
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            foreach (int stopAt in new[] { 5, 8 })
            {
                Scenario($"E-stop at step {stopAt} leaves a part on the lift: next Start sorts it first", () =>
                {
                    Kit1Rig rig = new Kit1Rig();
                    List<Material> parts = new List<Material>
                    {
                        Material.Plastic, Material.Metal, Material.Plastic, Material.Metal
                    };
                    rig.Plant.Load(parts);
                    rig.Plant.PressStart();
                    RunUntil(rig, r => r.Plc.HR_Step == stopAt, 30000);
                    rig.Plant.EStopPressed = true;
                    Run(rig, 1500);   // bistable valves: a moving cylinder finishes its stroke
                    Expect(rig.Plc.HR_Step == 90 && rig.Plc.HR_FaultCode == 1, "E-stop fault");
                    Expect(rig.Plant.OnPlatform.HasValue, "a part is left on the platform");
                    rig.Plant.EStopPressed = false;
                    rig.Plant.PressReset();
                    RunUntil(rig, r => r.Plc.HR_Step == 0, 30000);
                    rig.Plant.PressStart();
                    RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == parts.Count, 90000);
                    CheckBatch(rig, parts);
                });
            }

            Scenario("Start pressed while freshly loaded parts are still dropping runs the batch", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plant.Load(new[] { Material.Metal });
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == 1, 30000);
                Run(rig, 3000);   // magazine-empty flag now latched from the finished batch
                Expect(rig.Plc.IN_MagazineEmpty, "magazine-empty flag latched after the batch");
                List<Material> second = new List<Material> { Material.Plastic, Material.Metal };
                rig.Plant.Load(second);   // parts need 300 ms to reach the sensor
                rig.Plant.PressStart();   // operator presses Start straight away
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone == 3, 60000);
                Expect(rig.Plant.Magazine.Count == 0 && rig.Plc.HR_PlasticCount == 1 && rig.Plc.HR_MetalCount == 2,
                    "both freshly loaded parts sorted");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            });

            Scenario("Heartbeat advances roughly every 100 ms", () =>
            {
                // Self-resetting TON: period is 100 ms plus two scans (10 ms scans here,
                // ~1 ms on the Micro850), so 2 s gives 16-20 beats.
                Kit1Rig rig = new Kit1Rig();
                Run(rig, 2000);
                Expect(rig.Plc.HR_Heartbeat >= 15 && rig.Plc.HR_Heartbeat <= 21,
                    $"15-21 beats in 2 s (got {rig.Plc.HR_Heartbeat})");
                Expect(rig.Plc.HR_MapVersion == Kit1ModbusMap.MapVersion, "map version 1");
            });

            Scenario("EtherNet/IP reader variable names match Kit1_Modbus_Map.csv", () =>
            {
                string directory = AppContext.BaseDirectory;
                string csv = null;
                for (int up = 0; up < 8 && directory != null; up++)
                {
                    string candidate = System.IO.Path.Combine(directory, "PLC", "Kit1", "Kit1_Modbus_Map.csv");
                    if (System.IO.File.Exists(candidate))
                    {
                        csv = candidate;
                        break;
                    }

                    directory = System.IO.Path.GetDirectoryName(directory);
                }

                Expect(csv != null, "found PLC/Kit1/Kit1_Modbus_Map.csv");
                if (csv == null)
                {
                    return;
                }

                List<string> coilNames = new List<string>();
                List<string> registerNames = new List<string>();
                foreach (string line in System.IO.File.ReadAllLines(csv).Skip(1))
                {
                    string[] fields = line.Split(',');
                    if (fields.Length < 4)
                    {
                        continue;
                    }

                    (fields[2] == "coil" ? coilNames : registerNames).Add(fields[3]);
                }

                Expect(coilNames.SequenceEqual(Kit1ModbusMap.CoilVariables), "32 coil names in the same order");
                Expect(registerNames.SequenceEqual(Kit1ModbusMap.RegisterVariables), "10 register names in the same order");
            });

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures} check(s) failed)");
            return failures == 0 ? 0 : 1;
        }

        private static void CheckBatch(Kit1Rig rig, List<Material> parts)
        {
            int metal = parts.Count(m => m == Material.Metal);
            int plastic = parts.Count - metal;
            Expect(rig.Plc.HR_PartsDone == parts.Count, $"parts done {rig.Plc.HR_PartsDone} == {parts.Count}");
            Expect(rig.Plc.HR_MetalCount == metal, $"metal count {rig.Plc.HR_MetalCount} == {metal}");
            Expect(rig.Plc.HR_PlasticCount == plastic, $"plastic count {rig.Plc.HR_PlasticCount} == {plastic}");
            // Rig default (CFG_MetalToUpper = FALSE): plastic leaves at the upper level, metal at the lower.
            List<Material> metalTray = rig.Plc.CFG_MetalToUpper ? rig.Plant.UpperTray : rig.Plant.LowerTray;
            List<Material> plasticTray = rig.Plc.CFG_MetalToUpper ? rig.Plant.LowerTray : rig.Plant.UpperTray;
            Expect(metalTray.Count == metal && metalTray.All(m => m == Material.Metal),
                "metal tray holds every metal part and nothing else");
            Expect(plasticTray.Count == plastic && plasticTray.All(m => m == Material.Plastic),
                "plastic tray holds every plastic part and nothing else");
            Expect(rig.Plc.HR_FaultCode == 0, $"no fault (got {rig.Plc.HR_FaultCode})");
            Expect(rig.Plc.ST_Homed, "ends homed");
            Expect(rig.Plc.HR_LastCycleMs > 0, $"cycle time recorded ({rig.Plc.HR_LastCycleMs} ms)");
            Expect(rig.Plant.Violations.Count == 0, Violations(rig));
            Console.WriteLine($"    {parts.Count} parts ({metal} metal, {plastic} plastic), " +
                $"last cycle {rig.Plc.HR_LastCycleMs} ms, sim time {rig.NowMs / 1000.0:0.0} s");
        }

        private static string Violations(Kit1Rig rig)
        {
            return rig.Plant.Violations.Count == 0
                ? "no mechanical violations"
                : "violations: " + string.Join("; ", rig.Plant.Violations.Distinct());
        }

        private static void Scenario(string name, Action body)
        {
            Console.WriteLine($"- {name}");
            try
            {
                body();
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"    FAIL: {exception.Message}");
            }
        }

        private static void Expect(bool condition, string what)
        {
            if (!condition)
            {
                failures++;
                Console.WriteLine($"    FAIL: {what}");
            }
        }

        private static void Run(Kit1Rig rig, double ms)
        {
            for (double t = 0; t < ms; t += ScanMs)
            {
                rig.Step(ScanMs);
            }
        }

        private static void RunUntil(Kit1Rig rig, Func<Kit1Rig, bool> done, double timeoutMs)
        {
            for (double t = 0; t < timeoutMs; t += ScanMs)
            {
                rig.Step(ScanMs);
                if (done(rig))
                {
                    return;
                }
            }

            throw new TimeoutException(
                $"condition not reached in {timeoutMs / 1000} s (step {rig.Plc.HR_Step}, fault {rig.Plc.HR_FaultCode})");
        }

        private static List<Material> RandomParts(int count, int seed)
        {
            Random random = new Random(seed);
            List<Material> parts = Enumerable.Range(0, count)
                .Select(_ => random.Next(2) == 0 ? Material.Metal : Material.Plastic).ToList();
            // Guarantee both materials appear.
            parts[0] = Material.Metal;
            parts[1] = Material.Plastic;
            return parts;
        }

        // ------------------------------------------------------------------
        // linktest: real TCP, real time, Unity's ModbusTcpClient as the reader.
        // ------------------------------------------------------------------
        private static int LinkTest()
        {
            Kit1Rig rig = new Kit1Rig(travelMs: 150);
            List<Material> parts = RandomParts(4, seed: 21);
            rig.Plant.Load(parts);

            using ModbusTcpServer server = new ModbusTcpServer(
                IPAddress.Loopback, 0, rig.Sync, rig.Coils, rig.Registers, Kit1Rig.CoilWritable);
            server.Start();
            Console.WriteLine($"Soft PLC listening on 127.0.0.1:{server.Port}");

            bool stop = false;
            Thread plcThread = new Thread(() => ScanLoop(rig, () => stop)) { IsBackground = true };
            plcThread.Start();

            HashSet<Kit1PlcStep> stepsSeen = new HashSet<Kit1PlcStep>();
            HashSet<Kit1PlcMaterial> materialsSeen = new HashSet<Kit1PlcMaterial>();
            int polls = 0;
            ushort firstBeat = 0;
            Kit1PlcSnapshot last = null;
            bool sawSolenoid = false;
            bool sawBothCoils = false;
            Stopwatch clock = Stopwatch.StartNew();

            using (ModbusTcpClient client = new ModbusTcpClient("127.0.0.1", server.Port))
            {
                client.Connect();
                rig.Plant.PressStart();   // operator presses the green button
                while (clock.Elapsed.TotalSeconds < 40)
                {
                    bool[] coils = client.ReadCoils(Kit1ModbusMap.CoilStart, Kit1ModbusMap.CoilCount);
                    ushort[] registers = client.ReadHoldingRegisters(
                        Kit1ModbusMap.RegisterStart, Kit1ModbusMap.RegisterCount);
                    last = Kit1PlcSnapshot.Decode(coils, registers, DateTime.UtcNow);
                    if (polls == 0)
                    {
                        firstBeat = last.Heartbeat;
                    }

                    polls++;
                    stepsSeen.Add(last.Step);
                    materialsSeen.Add(last.Material);
                    sawSolenoid |= last.SolFeedExtend || last.SolLiftDown || last.SolUpperEjectorExtend;
                    sawBothCoils |= (last.SolFeedExtend && last.SolFeedRetract) ||
                        (last.SolLiftUp && last.SolLiftDown);
                    if (last.PartsCompleted == parts.Count && last.Step == Kit1PlcStep.Idle)
                    {
                        break;
                    }

                    Thread.Sleep(50);   // 20 Hz, same as the Unity link
                }

                // HMI command coil write (FC05) is only for HMIs; check the PLC clears it.
                Expect(WriteCoil(server.Port, Kit1ModbusMap.HmiReset, true), "FC05 write to HMI_Reset accepted");
                Expect(!WriteCoil(server.Port, Kit1ModbusMap.OutFeedExtend, true),
                    "FC05 write to an output coil rejected");
            }

            // Dispose() racing Connect() must never leave a socket open, and a
            // disposed client must refuse to reconnect (the twin's StopLink path).
            Random race = new Random(5);
            for (int round = 0; round < 200; round++)
            {
                ModbusTcpClient racer = new ModbusTcpClient("127.0.0.1", server.Port);
                Thread connector = new Thread(() =>
                {
                    try
                    {
                        racer.Connect();
                    }
                    catch (Exception)
                    {
                    }
                });
                connector.Start();
                Thread.SpinWait(race.Next(0, 200000));
                racer.Dispose();
                connector.Join();
            }

            bool refused = false;
            ModbusTcpClient disposedClient = new ModbusTcpClient("127.0.0.1", server.Port);
            disposedClient.Dispose();
            try
            {
                disposedClient.Connect();
            }
            catch (ObjectDisposedException)
            {
                refused = true;
            }

            Thread.Sleep(500);   // let the server notice closed sockets
            Expect(server.ActiveClients == 0,
                $"no connections left open after 200 dispose/connect races (open: {server.ActiveClients})");
            Expect(refused, "a disposed client refuses to reconnect");

            stop = true;
            plcThread.Join();

            int metal = parts.Count(m => m == Material.Metal);
            Console.WriteLine($"  {polls} polls in {clock.Elapsed.TotalSeconds:0.0} s, " +
                $"{server.RequestsServed} Modbus requests served");
            Console.WriteLine($"  steps seen: {string.Join(", ", stepsSeen.OrderBy(s => (int)s))}");
            Expect(last != null && last.MapVersion == Kit1ModbusMap.MapVersion, "map version 1 over the wire");
            Expect(last != null && last.Heartbeat != firstBeat, "heartbeat advanced");
            Expect(last != null && last.PartsCompleted == parts.Count,
                $"parts completed {last?.PartsCompleted} == {parts.Count}");
            Expect(last != null && last.MetalCount == metal && last.PlasticCount == parts.Count - metal,
                "metal/plastic counts decoded");
            foreach (Kit1PlcStep step in new[]
            {
                Kit1PlcStep.Homing, Kit1PlcStep.Feeding, Kit1PlcStep.Detect, Kit1PlcStep.UpperEject,
                Kit1PlcStep.LiftDown, Kit1PlcStep.LowerEject, Kit1PlcStep.LiftUp
            })
            {
                Expect(stepsSeen.Contains(step), $"observed step {step}");
            }

            Expect(materialsSeen.Contains(Kit1PlcMaterial.Metal) && materialsSeen.Contains(Kit1PlcMaterial.Plastic),
                "observed both material classifications");
            Expect(sawSolenoid, "observed solenoid outputs");
            Expect(!sawBothCoils, "never saw both coils of one valve on");
            Expect(last != null && last.Homed && last.EStopOk && !last.Faulted, "final state homed, healthy");

            Console.WriteLine(failures == 0 ? "LINKTEST PASS" : $"LINKTEST FAIL ({failures} check(s) failed)");
            return failures == 0 ? 0 : 1;
        }

        private static bool WriteCoil(int port, int address, bool value)
        {
            using TcpClient tcp = new TcpClient("127.0.0.1", port);
            NetworkStream stream = tcp.GetStream();
            byte[] request =
            {
                0, 99, 0, 0, 0, 6, 1, 0x05, (byte)(address >> 8), (byte)address,
                (byte)(value ? 0xFF : 0x00), 0
            };
            stream.Write(request, 0, request.Length);
            byte[] response = new byte[12];
            int read = stream.Read(response, 0, response.Length);
            return read >= 8 && response[7] == 0x05;
        }

        private static void ScanLoop(Kit1Rig rig, Func<bool> stop)
        {
            Stopwatch clock = Stopwatch.StartNew();
            double simulated = 0;
            while (!stop())
            {
                double now = clock.Elapsed.TotalMilliseconds;
                while (simulated + ScanMs <= now)
                {
                    rig.Step(ScanMs);
                    simulated += ScanMs;
                }

                Thread.Sleep(2);
            }
        }

        // ------------------------------------------------------------------
        // serve: interactive soft PLC for the Unity twin.
        // ------------------------------------------------------------------
        private static int Serve(string[] args)
        {
            int port = 502;
            IPAddress bind = IPAddress.Loopback;
            bool demoLoop = false;
            List<Material> fixedParts = null;
            for (int index = 1; index < args.Length; index++)
            {
                if (args[index] == "--port" && index + 1 < args.Length)
                {
                    port = int.Parse(args[index + 1]);
                }
                else if (args[index] == "--bind" && index + 1 < args.Length)
                {
                    bind = IPAddress.Parse(args[index + 1]);
                }
                else if (args[index] == "--demo")
                {
                    demoLoop = true;   // press Start now and reload the magazine whenever it runs dry
                }
                else if (args[index] == "--parts" && index + 1 < args.Length)
                {
                    // Exact magazine order, e.g. --parts MPMPMPMP (M = metal, P = plastic).
                    fixedParts = args[index + 1].ToUpperInvariant()
                        .Where(c => c == 'M' || c == 'P')
                        .Select(c => c == 'M' ? Material.Metal : Material.Plastic)
                        .ToList();
                }
            }

            Kit1Rig rig = new Kit1Rig(travelMs: 500);
            Func<List<Material>> nextBatch = () => fixedParts != null && fixedParts.Count > 0
                ? new List<Material>(fixedParts)
                : RandomParts(8, Environment.TickCount);
            rig.Plant.Load(nextBatch());

            ModbusTcpServer server;
            try
            {
                server = new ModbusTcpServer(bind, port, rig.Sync, rig.Coils, rig.Registers, Kit1Rig.CoilWritable);
                server.Start();
            }
            catch (SocketException exception)
            {
                Console.WriteLine($"Cannot listen on {bind}:{port} ({exception.SocketErrorCode}). Try --port 5020.");
                return 1;
            }

            bool stop = false;
            Thread plcThread = new Thread(() => ScanLoop(rig, () => stop)) { IsBackground = true };
            plcThread.Start();

            Console.WriteLine($"Kit 1 soft PLC on {bind}:{server.Port} (Modbus TCP, unit 1, map v{Kit1ModbusMap.MapVersion})");
            Console.WriteLine("Keys: S start  X stop  R reset  E e-stop toggle  C continuous toggle");
            Console.WriteLine("      L load 8 random  M add metal  P add plastic  J jam feed toggle  Q quit");

            if (demoLoop)
            {
                Console.WriteLine("Demo loop: Start pressed; magazine reloads automatically when empty.");
                lock (rig.Sync)
                {
                    rig.Plant.PressStart();
                }
            }

            string lastLine = string.Empty;
            bool interactive = !Console.IsInputRedirected;
            while (true)
            {
                if (demoLoop)
                {
                    lock (rig.Sync)
                    {
                        // Like an operator: reload when empty, then give one press of
                        // Start once a part has dropped to the magazine sensor.
                        if (rig.Plc.HR_Step == 0 && !rig.Plant.StartPressed)
                        {
                            if (rig.Plant.Magazine.Count == 0 && !rig.Plant.PartAtSlot)
                            {
                                rig.Plant.Load(nextBatch());
                            }
                            else if (rig.Plant.PartAtSlot)
                            {
                                rig.Plant.PressStart();
                            }
                        }
                    }
                }

                if (interactive && Console.KeyAvailable)
                {
                    ConsoleKey key = Console.ReadKey(true).Key;
                    lock (rig.Sync)
                    {
                        switch (key)
                        {
                            case ConsoleKey.S: rig.Plant.PressStart(); break;
                            case ConsoleKey.X: rig.Plant.PressStop(); break;
                            case ConsoleKey.R: rig.Plant.PressReset(); break;
                            case ConsoleKey.E: rig.Plant.EStopPressed = !rig.Plant.EStopPressed; break;
                            case ConsoleKey.C: rig.Plant.ContinuousSelector = !rig.Plant.ContinuousSelector; break;
                            case ConsoleKey.L: rig.Plant.Load(RandomParts(8, Environment.TickCount)); break;
                            case ConsoleKey.M: rig.Plant.Load(new[] { Material.Metal }); break;
                            case ConsoleKey.P: rig.Plant.Load(new[] { Material.Plastic }); break;
                            case ConsoleKey.J: rig.Plant.Feed.Stuck = !rig.Plant.Feed.Stuck; break;
                        }
                    }

                    if (key == ConsoleKey.Q)
                    {
                        break;
                    }
                }

                string line;
                lock (rig.Sync)
                {
                    Kit1Program p = rig.Plc;
                    line = $"step {(Kit1PlcStep)p.HR_Step,-12} fault {p.HR_FaultCode} " +
                        $"done {p.HR_PartsDone} (M{p.HR_MetalCount}/P{p.HR_PlasticCount}) " +
                        $"mag {rig.Plant.Magazine.Count} " +
                        $"{(rig.Plant.EStopPressed ? "ESTOP " : "")}{(rig.Plant.ContinuousSelector ? "cont" : "single")} " +
                        $"{(rig.Plant.Feed.Stuck ? "JAM " : "")}clients:{server.RequestsServed}";
                }

                if (line != lastLine)
                {
                    Console.WriteLine(line);
                    lastLine = line;
                }

                Thread.Sleep(100);
            }

            stop = true;
            server.Dispose();
            return 0;
        }
    }
}
