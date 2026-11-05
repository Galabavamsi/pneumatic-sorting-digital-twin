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
    }

    // Read-only Modbus TCP link from the Micro850 to the Kit 1 twin.
    //
    // While the link is ON the PLC is master: Kit1PlcIo takes its DI/DO from the
    // latest snapshot, the twin's own automatic sequence is blocked, and the
    // actuators animate from the real solenoid outputs. While OFF the twin runs
    // its offline simulation exactly as before.
    //
    // Config: Assets/StreamingAssets/Kit1PlcLink.json, overridable on the command
    // line with -plcHost <ip> -plcPort <n> -plcLive. F8 toggles the link at runtime.
    public sealed class Kit1PlcLink : MonoBehaviour
    {
        private const string ConfigFileName = "Kit1PlcLink.json";

        private readonly object sync = new object();
        private Kit1PlcLinkConfig config = new Kit1PlcLinkConfig();
        private Thread worker;
        private volatile bool workerRun;
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
        // The twin's own sequence must stay off in this mode.
        public static bool LinkRequested => Instance != null && Instance.workerRun;

        public Kit1PlcLinkConfig Config => config;
        public string LastError { get { lock (sync) { return lastError; } } }
        public int PollCount { get { lock (sync) { return pollCount; } } }

        public Kit1PlcLinkState State
        {
            get
            {
                if (!workerRun)
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
                    if (latest == null)
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
                if (workerRun)
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
            if (workerRun)
            {
                return;
            }

            lock (sync)
            {
                latest = null;
                lastError = string.Empty;
                pollCount = 0;
            }

            workerRun = true;
            workerState = Kit1PlcLinkState.Connecting;
            worker = new Thread(PollLoop) { IsBackground = true, Name = "Kit1 PLC link" };
            worker.Start();
            Debug.Log($"Kit 1 PLC link ON: polling {config.host}:{config.port} (unit {config.unitId}) " +
                $"at {config.pollHz} Hz, read-only.");
        }

        public void StopLink()
        {
            if (!workerRun)
            {
                return;
            }

            workerRun = false;
            worker?.Join(config.timeoutMs + 500);
            worker = null;
            workerState = Kit1PlcLinkState.Offline;
            Debug.Log("Kit 1 PLC link OFF: twin returns to offline simulation.");
        }

        private void OnDestroy()
        {
            StopLink();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void PollLoop()
        {
            int periodMs = Mathf.Max(10, 1000 / Mathf.Max(1, config.pollHz));
            ModbusTcpClient client = null;
            try
            {
                while (workerRun)
                {
                    Stopwatch cycle = Stopwatch.StartNew();
                    try
                    {
                        if (client == null || !client.Connected)
                        {
                            workerState = Kit1PlcLinkState.Connecting;
                            client?.Dispose();
                            client = new ModbusTcpClient(
                                config.host, config.port, (byte)config.unitId, config.timeoutMs);
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

                        workerState = Kit1PlcLinkState.Live;
                    }
                    catch (Exception exception) when (
                        exception is ModbusException || exception is IOException ||
                        exception is System.Net.Sockets.SocketException || exception is ObjectDisposedException ||
                        exception is InvalidOperationException)
                    {
                        bool firstError;
                        lock (sync)
                        {
                            firstError = lastError != exception.Message;
                            lastError = exception.Message;
                        }

                        if (firstError)
                        {
                            Debug.LogWarning($"Kit 1 PLC link error ({config.host}:{config.port}): " +
                                $"{exception.Message} Retrying every second.");
                        }

                        workerState = Kit1PlcLinkState.Error;
                        client?.Dispose();
                        client = null;
                        SleepWhileRunning(1000);   // back off before reconnecting
                        continue;
                    }

                    int remaining = periodMs - (int)cycle.ElapsedMilliseconds;
                    if (remaining > 0)
                    {
                        SleepWhileRunning(remaining);
                    }
                }
            }
            finally
            {
                client?.Dispose();
            }
        }

        private void SleepWhileRunning(int milliseconds)
        {
            int slept = 0;
            while (workerRun && slept < milliseconds)
            {
                Thread.Sleep(Math.Min(50, milliseconds - slept));
                slept += 50;
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
                else if (args[index] == "-plcPort" && index + 1 < args.Length &&
                         int.TryParse(args[index + 1], out int port))
                {
                    config.port = port;
                }
                else if (args[index] == "-plcLive")
                {
                    config.enabledOnStart = true;
                }
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
                $"{config.host}:{config.port}  <color=#9aa4ae>F8 {(workerRun ? "off" : "on")}</color>";

            Kit1PlcSnapshot snapshot = LatestAny;
            if (workerRun && snapshot != null)
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
            if (workerRun && !string.IsNullOrEmpty(error))
            {
                text += $"\n<color=#ff5a5a>{error}</color>";
            }

            float height = 12f + 22f * (1 + text.Split('\n').Length - 1);
            GUI.Box(new Rect(12f, Screen.height - height - 12f, 560f, height), text, badgeStyle);
        }
    }
}
