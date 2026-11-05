using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Kit1DigitalTwin.PlcLink
{
    public enum Kit1PlcLinkState
    {
        Offline,
        Connecting,
        Live,
        Stale,
        Error
    }

    [Serializable]
    public sealed class Kit1PlcLinkConfig
    {
        public bool enabledOnStart;
        public string host = "127.0.0.1";
        public int port = 502;
        public int unitId = 1;
        public int pollHz = 20;
        public int staleAfterMs = 1000;
        public int timeoutMs = 1000;

        // Returns null when the settings are usable, otherwise what is wrong.
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "host is empty";
            }

            if (port < 1 || port > 65535)
            {
                return $"port {port} is outside 1-65535";
            }

            if (unitId < 0 || unitId > 255)
            {
                return $"unitId {unitId} is outside 0-255";
            }

            if (pollHz < 1 || pollHz > 100)
            {
                return $"pollHz {pollHz} is outside 1-100";
            }

            if (timeoutMs < 50 || timeoutMs > 10000)
            {
                return $"timeoutMs {timeoutMs} is outside 50-10000";
            }

            if (staleAfterMs < 100 || staleAfterMs > 60000)
            {
                return $"staleAfterMs {staleAfterMs} is outside 100-60000";
            }

            return null;
        }
    }

    // Read-only Modbus TCP link from the Micro850 to the Kit 1 twin.
    //
    // While the link is ON the PLC is master: Kit1PlcIo takes its DI/DO from the
    // latest snapshot, the twin's own automatic sequence and manual keys are
    // blocked, and the actuators animate from the real solenoid outputs. If the
    // data goes stale the twin freezes. While OFF the twin runs its offline
    // simulation exactly as before.
    //
    // Config: Assets/StreamingAssets/Kit1PlcLink.json, overridable on the command
    // line with -plcHost <ip> -plcPort <n> -plcLive. F8 toggles the link at runtime.
    public sealed class Kit1PlcLink : MonoBehaviour
    {
        private const string ConfigFileName = "Kit1PlcLink.json";

        // One polling thread. Each owns its stop flag and socket, so a worker that
        // is slow to exit can never share state with its replacement.
        private sealed class PollWorker
        {
            public volatile bool Run = true;
            public ModbusTcpClient Client;   // guarded by lock (this)
            public Thread Thread;
        }

        private readonly object sync = new object();
        private Kit1PlcLinkConfig config = new Kit1PlcLinkConfig();
        private PollWorker current;          // guarded by sync
        private volatile bool linkOn;
        private Kit1PlcSnapshot latest;
        private long latestReceivedTicks;
        private long lastBeatChangeTicks;
        private ushort lastBeat;
        private string lastError = string.Empty;
        private volatile Kit1PlcLinkState workerState = Kit1PlcLinkState.Offline;
        private int pollCount;
        private GUIStyle badgeStyle;

        public static Kit1PlcLink Instance { get; private set; }

        // True while the user has the link switched on (even if data is stale).
        // The twin's own sequence and manual controls must stay off in this mode.
        public static bool LinkRequested => Instance != null && Instance.linkOn;

        public Kit1PlcLinkConfig Config => config;
        public string LastError { get { lock (sync) { return lastError; } } }
        public int PollCount { get { lock (sync) { return pollCount; } } }

        public Kit1PlcLinkState State
        {
            get
            {
                if (!linkOn)
                {
                    return Kit1PlcLinkState.Offline;
                }

                Kit1PlcLinkState state = workerState;
                if (state == Kit1PlcLinkState.Live && LatestFresh == null)
                {
                    return Kit1PlcLinkState.Stale;
                }

                return state;
            }
        }

        // Latest snapshot if it is fresh and the PLC heartbeat is moving; otherwise null.
        public Kit1PlcSnapshot LatestFresh
        {
            get
            {
                lock (sync)
                {
                    // A known connection error makes the data stale at once, without
                    // waiting for staleAfterMs to run out.
                    if (latest == null || !linkOn || workerState != Kit1PlcLinkState.Live)
                    {
                        return null;
                    }

                    long now = Stopwatch.GetTimestamp();
                    double ageMs = (now - latestReceivedTicks) * 1000.0 / Stopwatch.Frequency;
                    double beatAgeMs = (now - lastBeatChangeTicks) * 1000.0 / Stopwatch.Frequency;
                    return ageMs <= config.staleAfterMs && beatAgeMs <= config.staleAfterMs ? latest : null;
                }
            }
        }

        public Kit1PlcSnapshot LatestAny
        {
            get { lock (sync) { return latest; } }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindAnyObjectByType<Kit1PlcLink>() == null)
            {
                new GameObject("Kit 1 PLC Link").AddComponent<Kit1PlcLink>();
            }
        }

        private void Awake()
        {
            Instance = this;
            LoadConfig();
        }

        private void Start()
        {
            if (config.enabledOnStart)
            {
                StartLink();
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
            {
                if (linkOn)
                {
                    StopLink();
                }
                else
                {
                    StartLink();
                }
            }
        }

        public void StartLink()
        {
            if (linkOn)
            {
                return;
            }

            string problem = config.Validate();
            if (problem != null)
            {
                lock (sync)
                {
                    lastError = $"Invalid PLC link settings: {problem}.";
                }

                Debug.LogError($"Kit 1 PLC link not started. Invalid settings in {ConfigFileName} " +
                    $"or on the command line: {problem}.");
                return;
            }

            StopWorker();   // make sure nothing from a previous session is still publishing

            PollWorker worker = new PollWorker();
            worker.Thread = new Thread(() => PollLoop(worker)) { IsBackground = true, Name = "Kit1 PLC link" };
            lock (sync)
            {
                latest = null;
                lastError = string.Empty;
                pollCount = 0;
                current = worker;
            }

            linkOn = true;
            workerState = Kit1PlcLinkState.Connecting;
            worker.Thread.Start();
            Debug.Log($"Kit 1 PLC link ON: polling {config.host}:{config.port} (unit {config.unitId}) " +
                $"at {config.pollHz} Hz, read-only.");
        }

        public void StopLink()
        {
            if (!linkOn)
            {
                return;
            }

            linkOn = false;
            StopWorker();
            workerState = Kit1PlcLinkState.Offline;
            Debug.Log("Kit 1 PLC link OFF: twin returns to offline simulation.");
        }

        private void StopWorker()
        {
            PollWorker worker;
            lock (sync)
            {
                worker = current;
                current = null;
                latest = null;
            }

            if (worker == null)
            {
                return;
            }

            worker.Run = false;
            lock (worker)
            {
                // Closing the socket ends any blocking read immediately.
                worker.Client?.Dispose();
                worker.Client = null;
            }

            // Even if the join times out, the worker is detached: it can no longer
            // publish (it is not 'current') and exits at its next check of Run.
            worker.Thread?.Join(500);
        }

        private void OnDestroy()
        {
            linkOn = false;
            StopWorker();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private bool IsCurrent(PollWorker worker)
        {
            return worker.Run && ReferenceEquals(worker, current);
        }

        private void PollLoop(PollWorker worker)
        {
            int periodMs = Mathf.Max(10, 1000 / Mathf.Max(1, config.pollHz));
            try
            {
                while (worker.Run)
                {
                    Stopwatch cycle = Stopwatch.StartNew();
                    try
                    {
                        ModbusTcpClient client;
                        lock (worker)
                        {
                            if (!worker.Run)
                            {
                                break;
                            }

                            if (worker.Client == null || !worker.Client.Connected)
                            {
                                worker.Client?.Dispose();
                                worker.Client = new ModbusTcpClient(
                                    config.host, config.port, (byte)config.unitId, config.timeoutMs);
                            }

                            client = worker.Client;
                        }

                        if (!client.Connected)
                        {
                            SetWorkerState(worker, Kit1PlcLinkState.Connecting);
                            client.Connect();
                        }

                        bool[] coils = client.ReadCoils(Kit1ModbusMap.CoilStart, Kit1ModbusMap.CoilCount);
                        ushort[] registers = client.ReadHoldingRegisters(
                            Kit1ModbusMap.RegisterStart, Kit1ModbusMap.RegisterCount);
                        Kit1PlcSnapshot snapshot = Kit1PlcSnapshot.Decode(coils, registers, DateTime.UtcNow);

                        if (snapshot.MapVersion != Kit1ModbusMap.MapVersion)
                        {
                            throw new ModbusException(
                                $"PLC map version {snapshot.MapVersion}, twin expects {Kit1ModbusMap.MapVersion}. " +
                                "Check the Modbus mapping in CCW.");
                        }

                        long now = Stopwatch.GetTimestamp();
                        lock (sync)
                        {
                            if (!IsCurrent(worker))
                            {
                                break;   // replaced or stopped: never publish
                            }

                            if (latest == null || snapshot.Heartbeat != lastBeat)
                            {
                                lastBeat = snapshot.Heartbeat;
                                lastBeatChangeTicks = now;
                            }

                            latest = snapshot;
                            latestReceivedTicks = now;
                            pollCount++;
                            lastError = string.Empty;
                        }

                        if (workerState != Kit1PlcLinkState.Live)
                        {
                            Debug.Log($"Kit 1 PLC link LIVE: {config.host}:{config.port}, map v{snapshot.MapVersion}, " +
                                $"PLC step {snapshot.Step}, parts done {snapshot.PartsCompleted}.");
                        }

                        SetWorkerState(worker, Kit1PlcLinkState.Live);
                    }
                    catch (Exception exception)
                    {
                        if (!worker.Run)
                        {
                            break;   // StopLink closed the socket under us
                        }

                        // Network, protocol or unexpected errors all end up here: report
                        // once, drop the connection and retry rather than killing the thread.
                        bool firstError;
                        lock (sync)
                        {
                            if (!IsCurrent(worker))
                            {
                                break;
                            }

                            firstError = lastError != exception.Message;
                            lastError = exception.Message;
                        }

                        if (firstError)
                        {
                            Debug.LogWarning($"Kit 1 PLC link error ({config.host}:{config.port}): " +
                                $"{exception.GetType().Name}: {exception.Message} Retrying every second.");
                        }

                        SetWorkerState(worker, Kit1PlcLinkState.Error);
                        lock (worker)
                        {
                            worker.Client?.Dispose();
                            worker.Client = null;
                        }

                        SleepWhileRunning(worker, 1000);
                        continue;
                    }

                    int remaining = periodMs - (int)cycle.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        SleepWhileRunning(worker, remaining);
                    }
                }
            }
            finally
            {
                lock (worker)
                {
                    worker.Client?.Dispose();
                    worker.Client = null;
                }
            }
        }

        private void SetWorkerState(PollWorker worker, Kit1PlcLinkState state)
        {
            lock (sync)
            {
                if (IsCurrent(worker))
                {
                    workerState = state;
                }
            }
        }

        private static void SleepWhileRunning(PollWorker worker, int milliseconds)
        {
            Stopwatch slept = Stopwatch.StartNew();
            while (worker.Run && slept.ElapsedMilliseconds < milliseconds)
            {
                Thread.Sleep((int)Math.Min(20, Math.Max(1, milliseconds - slept.ElapsedMilliseconds)));
            }
        }

        private void LoadConfig()
        {
            string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
            try
            {
                if (File.Exists(path))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), config);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read {path}: {exception.Message}. Using defaults.");
            }

            string[] args = Environment.GetCommandLineArgs();
            for (int index = 0; index < args.Length; index++)
            {
                if (args[index] == "-plcHost" && index + 1 < args.Length)
                {
                    config.host = args[index + 1];
                }
                else if (args[index] == "-plcPort" && index + 1 < args.Length)
                {
                    if (int.TryParse(args[index + 1], out int port))
                    {
                        config.port = port;   // range is checked by Validate() in StartLink
                    }
                    else
                    {
                        Debug.LogError($"-plcPort '{args[index + 1]}' is not a number; keeping port {config.port}.");
                    }
                }
                else if (args[index] == "-plcLive")
                {
                    config.enabledOnStart = true;
                }
            }

            string problem = config.Validate();
            if (problem != null)
            {
                Debug.LogError($"Kit 1 PLC link settings are invalid ({problem}); the link will not start until fixed.");
            }
        }

        private void OnGUI()
        {
            badgeStyle ??= new GUIStyle(GUI.skin.box)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                richText = true,
                padding = new RectOffset(10, 10, 6, 6)
            };

            Kit1PlcLinkState state = State;
            string colour = state switch
            {
                Kit1PlcLinkState.Live => "#3dff6a",
                Kit1PlcLinkState.Stale => "#ffc23d",
                Kit1PlcLinkState.Error => "#ff5a5a",
                Kit1PlcLinkState.Connecting => "#ffc23d",
                _ => "#9aa4ae"
            };

            string text = $"<b>PLC LINK</b> <color={colour}>{state.ToString().ToUpperInvariant()}</color>  " +
                $"{config.host}:{config.port}  <color=#9aa4ae>F8 {(linkOn ? "off" : "on")}</color>";

            Kit1PlcSnapshot snapshot = LatestAny;
            if (linkOn && snapshot != null)
            {
                text += $"\nstep {snapshot.Step}  done {snapshot.PartsCompleted} " +
                    $"(metal {snapshot.MetalCount} / plastic {snapshot.PlasticCount})  " +
                    $"last cycle {snapshot.LastCycleTimeMs} ms";
                if (snapshot.FaultCode != Kit1PlcFault.None)
                {
                    text += $"\n<color=#ff5a5a>FAULT {(int)snapshot.FaultCode}: {snapshot.FaultCode}</color>";
                }
                else if (!snapshot.EStopOk)
                {
                    text += "\n<color=#ff5a5a>E-STOP</color>";
                }
            }

            string error = LastError;
            if (!string.IsNullOrEmpty(error))
            {
                text += $"\n<color=#ff5a5a>{error}</color>";
            }

            float height = 12f + 22f * text.Split('\n').Length;
            GUI.Box(new Rect(12f, Screen.height - height - 12f, 560f, height), text, badgeStyle);
        }
    }
}
