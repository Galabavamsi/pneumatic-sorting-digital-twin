using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kit1DigitalTwin.Hmi
{
    public sealed class DigitalTwinHistorian : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 0.1f;

        private readonly List<TelemetryFrame> frames = new List<TelemetryFrame>();
        private Kit1AutomaticSequence sequence;
        private Kit1PlcIo plcIo;
        private Kit1SensorSimulation sensors;
        private Kit1MagazineBatch magazine;
        private ProductionMonitor production;
        private ElectropneumaticUtilities utilities;
        private PneumaticCylinderTest cylinder1;
        private SecondCylinderTest cylinder2;
        private ThirdCylinderTest cylinder3;
        private LiftCylinderTest lift;
        private float recordingStartedAt;
        private float nextSampleAt;
        private float replayTime;
        private float previousTimeScale = 1f;
        private int replayIndex;
        private TelemetryFrame replayFrame;

        public bool IsRecording { get; private set; }
        public bool IsReplaying { get; private set; }
        public int SampleCount => frames.Count;
        public float DurationSeconds => frames.Count > 0 ? frames[frames.Count - 1].ElapsedSeconds : 0f;
        public float ReplaySeconds => replayTime;
        public float ReplayNormalized => DurationSeconds > 0f ? replayTime / DurationSeconds : 0f;
        public string ReplayState => IsReplaying ? replayFrame.SequenceState : "--";
        public string ReplayMaterial => IsReplaying ? replayFrame.Material : "--";
        public string ReplayInputs => IsReplaying ? replayFrame.InputBits : "--";
        public string ReplayOutputs => IsReplaying ? replayFrame.OutputBits : "--";
        public float ReplayPressureBar => IsReplaying ? replayFrame.PressureBar : 0f;
        public string LastExportPath { get; private set; } = string.Empty;
        public string StatusText { get; private set; } = "READY";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<DigitalTwinHistorian>() == null)
            {
                new GameObject("Digital Twin Historian").AddComponent<DigitalTwinHistorian>();
            }
        }

        private void Start()
        {
            sequence = FindAnyObjectByType<Kit1AutomaticSequence>();
            plcIo = FindAnyObjectByType<Kit1PlcIo>();
            sensors = FindAnyObjectByType<Kit1SensorSimulation>();
            magazine = FindAnyObjectByType<Kit1MagazineBatch>();
            production = FindAnyObjectByType<ProductionMonitor>();
            utilities = FindAnyObjectByType<ElectropneumaticUtilities>();
            cylinder1 = FindAnyObjectByType<PneumaticCylinderTest>();
            cylinder2 = FindAnyObjectByType<SecondCylinderTest>();
            cylinder3 = FindAnyObjectByType<ThirdCylinderTest>();
            lift = FindAnyObjectByType<LiftCylinderTest>();
        }

        private void Update()
        {
            if (IsRecording && Time.realtimeSinceStartup >= nextSampleAt)
            {
                CaptureFrame();
                nextSampleAt = Time.realtimeSinceStartup + SampleIntervalSeconds;
            }

            if (!IsReplaying)
            {
                return;
            }

            replayTime += Time.unscaledDeltaTime;
            if (replayTime >= DurationSeconds)
            {
                replayTime = DurationSeconds;
                ApplyReplayFrame();
                StopReplay();
                return;
            }

            ApplyReplayFrame();
        }

        private void OnDisable()
        {
            if (IsReplaying)
            {
                Time.timeScale = previousTimeScale;
                IsReplaying = false;
            }
        }

        public void StartRecording()
        {
            if (IsReplaying)
            {
                StopReplay();
            }

            frames.Clear();
            recordingStartedAt = Time.realtimeSinceStartup;
            nextSampleAt = recordingStartedAt;
            IsRecording = true;
            StatusText = "RECORDING";
            production?.RecordEvent("Historian recording started.", ProductionEventSeverity.Info);
            CaptureFrame();
        }

        public void StopRecording()
        {
            if (!IsRecording)
            {
                return;
            }

            CaptureFrame();
            IsRecording = false;
            StatusText = "RECORDED";
            production?.RecordEvent(
                $"Historian recording stopped: {frames.Count} samples.",
                ProductionEventSeverity.Info);
        }

        public bool StartReplay()
        {
            if (IsRecording || frames.Count < 2 || (sequence != null && sequence.IsRunningInstance))
            {
                StatusText = "STOP MACHINE / RECORDING FIRST";
                return false;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            replayTime = 0f;
            replayIndex = 0;
            IsReplaying = true;
            StatusText = "REPLAYING";
            production?.RecordEvent("Historian replay started.", ProductionEventSeverity.Info);
            ApplyReplayFrame();
            return true;
        }

        public void StopReplay()
        {
            if (!IsReplaying)
            {
                return;
            }

            IsReplaying = false;
            Time.timeScale = previousTimeScale;
            StatusText = "REPLAY READY";
            plcIo?.ForceSafeHome();
            production?.RecordEvent("Historian replay stopped.", ProductionEventSeverity.Info);
        }

        public void SetReplayNormalized(float normalizedTime)
        {
            if (!IsReplaying || frames.Count < 2)
            {
                return;
            }

            replayTime = Mathf.Clamp01(normalizedTime) * DurationSeconds;
            replayIndex = 0;
            ApplyReplayFrame();
        }

        public void ClearRecording()
        {
            if (IsRecording || IsReplaying)
            {
                return;
            }

            frames.Clear();
            replayTime = 0f;
            StatusText = "READY";
            production?.RecordEvent("Historian memory cleared.", ProductionEventSeverity.Info);
        }

        public bool ExportCsv()
        {
            if (frames.Count == 0)
            {
                StatusText = "NOTHING TO EXPORT";
                return false;
            }

            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "DigitalTwinHistory");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"Kit1_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                StringBuilder csv = new StringBuilder();
                csv.AppendLine(
                    "timestamp,elapsed_s,state,material,remaining,completed,metal,plastic,aborted," +
                    "control_voltage_v,pressure_bar,emergency_stop,inputs,outputs," +
                    "cylinder1,cylinder2,cylinder3,lift,event");
                foreach (TelemetryFrame frame in frames)
                {
                    csv.Append(Escape(frame.Timestamp)).Append(',')
                        .Append(frame.ElapsedSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(Escape(frame.SequenceState)).Append(',')
                        .Append(Escape(frame.Material)).Append(',')
                        .Append(frame.Remaining).Append(',')
                        .Append(frame.Completed).Append(',')
                        .Append(frame.Metal).Append(',')
                        .Append(frame.Plastic).Append(',')
                        .Append(frame.Aborted).Append(',')
                        .Append(frame.ControlVoltage.ToString("0.0", CultureInfo.InvariantCulture)).Append(',')
                        .Append(frame.PressureBar.ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                        .Append(frame.EmergencyStop ? "1" : "0").Append(',')
                        .Append(Escape(frame.InputBits)).Append(',')
                        .Append(Escape(frame.OutputBits)).Append(',')
                        .Append(frame.Cylinder1.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(frame.Cylinder2.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(frame.Cylinder3.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(frame.Lift.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
                        .Append(Escape(frame.EventMessage)).AppendLine();
                }

                File.WriteAllText(path, csv.ToString(), Encoding.UTF8);
                LastExportPath = path;
                StatusText = "CSV EXPORTED";
                production?.RecordEvent($"Historian exported {frames.Count} samples.", ProductionEventSeverity.Success);
                Debug.Log($"Digital twin historian exported: {path}");
                return true;
            }
            catch (Exception exception)
            {
                StatusText = "EXPORT FAILED";
                Debug.LogException(exception);
                production?.RecordAlarm("Historian CSV export failed.");
                return false;
            }
        }

        private void CaptureFrame()
        {
            float elapsed = Mathf.Max(0f, Time.realtimeSinceStartup - recordingStartedAt);
            string latestEvent = production != null && production.EventCount > 0
                ? production.GetEvent(0).Message
                : string.Empty;
            frames.Add(new TelemetryFrame
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                ElapsedSeconds = elapsed,
                SequenceState = sequence != null ? sequence.CurrentStateName : "UNKNOWN",
                Material = sensors != null ? sensors.SelectedMaterial.ToString().ToUpperInvariant() : "UNKNOWN",
                Remaining = magazine != null ? magazine.RemainingCount : 0,
                Completed = production != null ? production.CompletedCycles : 0,
                Metal = production != null ? production.MetalCount : 0,
                Plastic = production != null ? production.PlasticCount : 0,
                Aborted = production != null ? production.AbortedCycles : 0,
                ControlVoltage = utilities != null && utilities.ControlPowerAvailable ? 24f : 0f,
                PressureBar = utilities != null ? utilities.EffectivePressureBar : 0f,
                EmergencyStop = utilities != null && utilities.EmergencyStopActive,
                InputBits = BuildInputBits(),
                OutputBits = BuildOutputBits(),
                Cylinder1 = cylinder1 != null ? cylinder1.NormalizedPosition : 0f,
                Cylinder2 = cylinder2 != null ? cylinder2.NormalizedPosition : 0f,
                Cylinder3 = cylinder3 != null ? cylinder3.NormalizedPosition : 0f,
                Lift = lift != null ? lift.NormalizedPosition : 0f,
                EventMessage = latestEvent
            });
        }

        private void ApplyReplayFrame()
        {
            if (frames.Count == 0)
            {
                return;
            }

            while (replayIndex < frames.Count - 2 &&
                frames[replayIndex + 1].ElapsedSeconds <= replayTime)
            {
                replayIndex++;
            }

            TelemetryFrame first = frames[replayIndex];
            TelemetryFrame second = frames[Mathf.Min(replayIndex + 1, frames.Count - 1)];
            float interval = second.ElapsedSeconds - first.ElapsedSeconds;
            float blend = interval > Mathf.Epsilon
                ? Mathf.Clamp01((replayTime - first.ElapsedSeconds) / interval)
                : 0f;
            replayFrame = blend < 0.5f ? first : second;
            cylinder1?.ApplyReplayPosition(Mathf.Lerp(first.Cylinder1, second.Cylinder1, blend));
            cylinder2?.ApplyReplayPosition(Mathf.Lerp(first.Cylinder2, second.Cylinder2, blend));
            cylinder3?.ApplyReplayPosition(Mathf.Lerp(first.Cylinder3, second.Cylinder3, blend));
            lift?.ApplyReplayPosition(Mathf.Lerp(first.Lift, second.Lift, blend));
        }

        private string BuildInputBits()
        {
            if (plcIo == null)
            {
                return "";
            }

            return Bits(
                plcIo.DI_MagazinePartPresent, plcIo.DI_MagazineEmpty, plcIo.DI_PartOnLift,
                plcIo.DI_MetalDetected, plcIo.DI_PlasticDetected, plcIo.DI_LiftUp,
                plcIo.DI_LiftDown, plcIo.DI_Cylinder1Extended, plcIo.DI_Cylinder1Retracted,
                plcIo.DI_Cylinder2Extended, plcIo.DI_Cylinder2Retracted,
                plcIo.DI_Cylinder3Extended, plcIo.DI_Cylinder3Retracted,
                plcIo.DI_WorkpieceReleased);
        }

        private string BuildOutputBits()
        {
            return plcIo == null
                ? ""
                : Bits(plcIo.DO_Cylinder1Extend, plcIo.DO_Cylinder2Extend,
                    plcIo.DO_Cylinder3Extend, plcIo.DO_LiftUp);
        }

        private static string Bits(params bool[] values)
        {
            StringBuilder bits = new StringBuilder(values.Length);
            foreach (bool value in values)
            {
                bits.Append(value ? '1' : '0');
            }
            return bits.ToString();
        }

        private static string Escape(string value)
        {
            string safe = value ?? string.Empty;
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }

        private struct TelemetryFrame
        {
            public string Timestamp;
            public float ElapsedSeconds;
            public string SequenceState;
            public string Material;
            public int Remaining;
            public int Completed;
            public int Metal;
            public int Plastic;
            public int Aborted;
            public float ControlVoltage;
            public float PressureBar;
            public bool EmergencyStop;
            public string InputBits;
            public string OutputBits;
            public float Cylinder1;
            public float Cylinder2;
            public float Cylinder3;
            public float Lift;
            public string EventMessage;
        }
    }
}
