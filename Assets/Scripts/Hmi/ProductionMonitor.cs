using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kit1DigitalTwin.Hmi
{
    public enum ProductionEventSeverity
    {
        Info,
        Success,
        Warning,
        Alarm
    }

    public readonly struct ProductionEvent
    {
        public ProductionEvent(string timestamp, string message, ProductionEventSeverity severity)
        {
            Timestamp = timestamp;
            Message = message;
            Severity = severity;
        }

        public string Timestamp { get; }
        public string Message { get; }
        public ProductionEventSeverity Severity { get; }
    }

    /// <summary>
    /// Kit-neutral runtime production ledger. Individual kit controllers publish
    /// semantic events here; HMI surfaces consume the resulting counters and log.
    /// </summary>
    public sealed class ProductionMonitor : MonoBehaviour
    {
        private const int MaximumEvents = 40;

        private readonly List<ProductionEvent> events = new List<ProductionEvent>();
        private float activeCycleStartedAt;
        private float accumulatedCycleSeconds;
        private string activeMaterial = string.Empty;
        private bool cycleActive;

        public int CompletedCycles { get; private set; }
        public int MetalCount { get; private set; }
        public int PlasticCount { get; private set; }
        public int AbortedCycles { get; private set; }
        public float LastCycleSeconds { get; private set; }
        public float AverageCycleSeconds => CompletedCycles > 0
            ? accumulatedCycleSeconds / CompletedCycles
            : 0f;
        public bool CycleActive => cycleActive;
        public int EventCount => events.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (KitSceneContext.IsKit1Scene && FindAnyObjectByType<ProductionMonitor>() == null)
            {
                new GameObject("Production Monitor").AddComponent<ProductionMonitor>();
            }
        }

        private void Start()
        {
            RecordEvent("Production monitoring online.", ProductionEventSeverity.Info);
        }

        public ProductionEvent GetEvent(int index)
        {
            return index >= 0 && index < events.Count
                ? events[index]
                : default;
        }

        public void BeginPart(string material, int remainingWorkpieces)
        {
            if (cycleActive)
            {
                AbortPart("Previous cycle superseded.");
            }

            activeMaterial = string.IsNullOrWhiteSpace(material) ? "UNKNOWN" : material.ToUpperInvariant();
            activeCycleStartedAt = Time.realtimeSinceStartup;
            cycleActive = true;
            RecordEvent(
                $"{activeMaterial} cycle started; {remainingWorkpieces} in magazine.",
                ProductionEventSeverity.Info);
        }

        public void RecordDetection(string route)
        {
            RecordEvent($"{activeMaterial} detected; routing {route}.", ProductionEventSeverity.Info);
        }

        public void CompletePart()
        {
            if (!cycleActive)
            {
                return;
            }

            LastCycleSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - activeCycleStartedAt);
            accumulatedCycleSeconds += LastCycleSeconds;
            CompletedCycles++;
            if (activeMaterial == "METAL")
            {
                MetalCount++;
            }
            else if (activeMaterial == "PLASTIC")
            {
                PlasticCount++;
            }

            RecordEvent(
                $"{activeMaterial} sorted successfully in {LastCycleSeconds:0.00} s.",
                ProductionEventSeverity.Success);
            cycleActive = false;
            activeMaterial = string.Empty;
        }

        public void AbortPart(string reason)
        {
            if (cycleActive)
            {
                AbortedCycles++;
                cycleActive = false;
                activeMaterial = string.Empty;
            }

            RecordEvent(reason, ProductionEventSeverity.Warning);
        }

        public void RecordEvent(string message, ProductionEventSeverity severity)
        {
            events.Insert(0, new ProductionEvent(
                DateTime.Now.ToString("HH:mm:ss"),
                message,
                severity));
            if (events.Count > MaximumEvents)
            {
                events.RemoveAt(events.Count - 1);
            }
        }

        public void RecordAlarm(string message)
        {
            RecordEvent(message, ProductionEventSeverity.Alarm);
        }

        public void ResetCounters()
        {
            CompletedCycles = 0;
            MetalCount = 0;
            PlasticCount = 0;
            AbortedCycles = 0;
            LastCycleSeconds = 0f;
            accumulatedCycleSeconds = 0f;
            events.Clear();
            RecordEvent("Production counters reset.", ProductionEventSeverity.Info);
        }
    }
}
