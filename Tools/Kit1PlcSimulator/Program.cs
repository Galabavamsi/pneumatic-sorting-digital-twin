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
                default:
                    Console.WriteLine("Usage: Kit1PlcSimulator [serve [--port N] [--bind IP] | selftest | linktest]");
                    return 2;
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

            Scenario("Metal routed to the lower tray when CFG_MetalToUpper = FALSE", () =>
            {
                Kit1Rig rig = new Kit1Rig();
                rig.Plc.CFG_MetalToUpper = false;
                List<Material> parts = RandomParts(6, seed: 3);
                rig.Plant.Load(parts);
                rig.Plant.PressStart();
                RunUntil(rig, r => r.Plc.HR_Step == 0 && r.Plc.HR_PartsDone > 0, 90000);
                Expect(rig.Plant.LowerTray.All(m => m == Material.Metal), "lower tray holds only metal");
                Expect(rig.Plant.UpperTray.All(m => m == Material.Plastic), "upper tray holds only plastic");
                Expect(rig.Plant.Violations.Count == 0, Violations(rig));
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
            Expect(rig.Plant.UpperTray.Count == metal && rig.Plant.UpperTray.All(m => m == Material.Metal),
                "upper tray holds every metal part and nothing else");
            Expect(rig.Plant.LowerTray.Count == plastic && rig.Plant.LowerTray.All(m => m == Material.Plastic),
                "lower tray holds every plastic part and nothing else");
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
                        if (rig.Plc.HR_Step == 0 && rig.Plant.Magazine.Count == 0 && !rig.Plant.PartAtSlot)
                        {
                            rig.Plant.Load(nextBatch());
                            rig.Plant.PressStart();
                        }
                        else if (rig.Plc.HR_Step == 0 && rig.Plant.Magazine.Count > 0)
                        {
                            rig.Plant.PressStart();
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
