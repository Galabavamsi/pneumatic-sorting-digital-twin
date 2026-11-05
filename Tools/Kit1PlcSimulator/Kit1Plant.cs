using System;
using System.Collections.Generic;

namespace Kit1PlcSimulator
{
    public enum Material
    {
        Metal,
        Plastic
    }

    // Double-acting cylinder on a 5/2 bistable double-solenoid valve.
    public sealed class Cylinder
    {
        public readonly string Name;
        public double Position;          // 0 = retracted, 1 = extended
        public double TravelMs;
        public bool Stuck;
        private bool valveExtend;        // bistable spool state

        public Cylinder(string name, double travelMs, double startPosition, bool startExtend)
        {
            Name = name;
            TravelMs = travelMs;
            Position = startPosition;
            valveExtend = startExtend;
        }

        public bool Moving { get; private set; }

        public void Update(bool coilExtend, bool coilRetract, bool airOn, double dtMs, List<string> violations)
        {
            if (coilExtend && coilRetract)
            {
                violations.Add($"{Name}: both solenoids energised");
            }
            else if (coilExtend)
            {
                valveExtend = true;
            }
            else if (coilRetract)
            {
                valveExtend = false;
            }

            double target = valveExtend ? 1.0 : 0.0;
            double before = Position;
            if (airOn && !Stuck)
            {
                double step = dtMs / TravelMs;
                Position = Math.Abs(target - Position) <= step ? target : Position + Math.Sign(target - Position) * step;
            }

            Moving = Math.Abs(Position - before) > 1e-9;
        }

        public bool Extended => Position >= 0.98;
        public bool Retracted => Position <= 0.02;
    }

    // Kit 1 mechanics: gravity magazine, feed (1A), lift (4A), upper ejector (2A),
    // lower ejector (3A), inductive IS-01 and capacitive CS-01 at the upper level,
    // two trays. Records mechanical violations so tests can fail on them.
    public sealed class Kit1Plant
    {
        public readonly Cylinder Feed;
        public readonly Cylinder UpperEjector;
        public readonly Cylinder LowerEjector;
        public readonly Cylinder Lift;          // extended = platform up

        public readonly Queue<Material> Magazine = new Queue<Material>();
        public readonly List<Material> UpperTray = new List<Material>();
        public readonly List<Material> LowerTray = new List<Material>();
        public readonly List<string> Violations = new List<string>();

        public bool ReedsFitted = true;
        public bool AirOn = true;
        public bool EStopPressed;
        public bool ContinuousSelector = true;

        private Material? onPlatform;
        private bool slotOccupied;
        private double dropTimerMs;
        private double startPressMs, stopPressMs, resetPressMs;

        public Kit1Plant(double travelMs = 400)
        {
            Feed = new Cylinder("1A feed", travelMs, 0, false);
            UpperEjector = new Cylinder("2A upper ejector", travelMs, 0, false);
            LowerEjector = new Cylinder("3A lower ejector", travelMs, 0, false);
            // Lift starts down so every test exercises homing.
            Lift = new Cylinder("4A lift", travelMs, 0, false);
        }

        public Material? OnPlatform => onPlatform;
        public bool PartAtSlot => slotOccupied;

        public void Load(IEnumerable<Material> parts)
        {
            foreach (Material part in parts)
            {
                Magazine.Enqueue(part);
            }

            if (!slotOccupied && Magazine.Count > 0)
            {
                dropTimerMs = 300;
            }
        }

        public void PressStart() => startPressMs = 200;
        public void PressStop() => stopPressMs = 200;
        public void PressReset() => resetPressMs = 200;

        public void Update(bool[] outputs, double dtMs)
        {
            bool liftWasUp = Lift.Extended;
            bool liftWasDown = Lift.Retracted;

            Feed.Update(outputs[0], outputs[1], AirOn, dtMs, Violations);
            UpperEjector.Update(outputs[2], outputs[3], AirOn, dtMs, Violations);
            LowerEjector.Update(outputs[4], outputs[5], AirOn, dtMs, Violations);
            Lift.Update(outputs[6], outputs[7], AirOn, dtMs, Violations);

            if (Lift.Moving && (UpperEjector.Position > 0.1 || LowerEjector.Position > 0.1))
            {
                Violations.Add("lift moved while an ejector was out");
            }

            if (Lift.Moving && Feed.Position > 0.1)
            {
                Violations.Add("lift moved while the feed pusher was out");
            }

            // Magazine refill of the feed slot under gravity.
            if (!slotOccupied && Magazine.Count > 0 && Feed.Position < 0.1)
            {
                dropTimerMs -= dtMs;
                if (dropTimerMs <= 0)
                {
                    slotOccupied = true;
                }
            }

            // Feed pushes the slot part onto the platform.
            if (slotOccupied && Feed.Position >= 0.95)
            {
                if (!liftWasUp)
                {
                    Violations.Add("feed pushed a part while the platform was not up");
                }
                else if (onPlatform.HasValue)
                {
                    Violations.Add("feed pushed a part onto an occupied platform");
                }
                else
                {
                    onPlatform = Magazine.Dequeue();
                    slotOccupied = false;
                    dropTimerMs = 300;
                }
            }

            // Ejectors clear the platform at their level.
            if (onPlatform.HasValue && UpperEjector.Position >= 0.9)
            {
                if (liftWasUp)
                {
                    UpperTray.Add(onPlatform.Value);
                    onPlatform = null;
                }
            }

            if (onPlatform.HasValue && LowerEjector.Position >= 0.9)
            {
                if (liftWasDown)
                {
                    LowerTray.Add(onPlatform.Value);
                    onPlatform = null;
                }
            }

            startPressMs = Math.Max(0, startPressMs - dtMs);
            stopPressMs = Math.Max(0, stopPressMs - dtMs);
            resetPressMs = Math.Max(0, resetPressMs - dtMs);
        }

        // Fill the PLC input image (DI-0..DI-15) with the wiring in Kit1_Sort.st.
        public void WriteInputs(bool[] di)
        {
            bool atUpper = Lift.Extended;
            di[0] = startPressMs > 0;
            di[1] = stopPressMs > 0;
            di[2] = resetPressMs > 0;
            di[3] = !EStopPressed;   // NC contact
            di[4] = atUpper && onPlatform == Material.Metal;
            di[5] = atUpper && onPlatform.HasValue;   // capacitive sees metal too
            di[6] = slotOccupied;
            di[7] = ContinuousSelector;
            di[8] = ReedsFitted && Feed.Retracted;
            di[9] = ReedsFitted && Feed.Extended;
            di[10] = ReedsFitted && UpperEjector.Retracted;
            di[11] = ReedsFitted && UpperEjector.Extended;
            di[12] = ReedsFitted && LowerEjector.Retracted;
            di[13] = ReedsFitted && LowerEjector.Extended;
            di[14] = ReedsFitted && Lift.Retracted;
            di[15] = ReedsFitted && Lift.Extended;
        }
    }
}
