(* =====================================================================
   Kit 1 metal / plastic sorting  -  Micro850 (2080-L50E-48QBB), CCW
   Program POU name: Kit1_Sort   (Structured Text)

   Variables: create them first from PLC/Kit1/Kit1_Variables.csv
     - "Global" rows go in the controller's Global Variables
       (only globals can be Modbus-mapped)
     - "Local" rows go in this program's Local Variables

   The simulator in Tools/Kit1PlcSimulator/Kit1Program.cs is a line-by-line
   C# port of this file. Change both together.

   Step numbers (HR_Step / Modbus 40002):
     0 Idle        1 Homing       2 WaitPart     3 Feeding
     4 FeedRetract 5 Detect       6 UpperEject   7 UpperRetract
     8 LiftDown    9 LowerEject  10 LowerRetract 11 LiftUp
    12 CycleDone  13 Stopping    90 Fault
   Fault codes (HR_FaultCode / Modbus 40007):
     1 E-stop                 2 feed extend timeout   3 feed retract timeout
     4 part fed but not seen  5 upper ejector timeout 6 lift down timeout
     7 lower ejector timeout  8 lift up timeout       9 home timeout
   ===================================================================== *)

(* ---------------------------------------------------------------------
   1. PHYSICAL INPUTS -> LOGICAL INPUTS
      Patch-panel jack DI-n is _IO_EM_DI_nn. DI-0..DI-8 are as wired on
      the rig on 2026-11-10 (Kit1_IO_Allocation.csv).
      If you patch differently, change ONLY this section.
   --------------------------------------------------------------------- *)
rawFeedRet        := _IO_EM_DI_00;   (* 1A reed, retracted  (input lights 0, 2, 4 are on with all three pulled in) *)
rawFeedExt        := _IO_EM_DI_01;   (* 1A reed, extended     *)
rawUpperRet       := _IO_EM_DI_02;   (* 2A reed, retracted    *)
rawUpperExt       := _IO_EM_DI_03;   (* 2A reed, extended     *)
rawLowerRet       := _IO_EM_DI_04;   (* 3A reed, retracted    *)
rawLowerExt       := _IO_EM_DI_05;   (* 3A reed, extended     *)
IN_Capacitive     := _IO_EM_DI_06;   (* CS-01, PNP NO, any part *)
IN_Inductive      := _IO_EM_DI_07;   (* IS-01, PNP NO, metal  *)
rawMagPart        := _IO_EM_DI_08;   (* BTS-01 light sensor at the magazine base *)
IN_StartPB        := _IO_EM_DI_09;   (* green PB, NO contact  *)
IN_StopPB         := _IO_EM_DI_10;   (* red PB,   NO contact  *)
IN_ResetPB        := _IO_EM_DI_11;   (* black PB, NO contact  *)
rawEStop          := _IO_EM_DI_12;   (* E-stop contact        *)
IN_ContinuousMode := _IO_EM_DI_13;   (* selector: ON = run until empty, OFF = one part per Start *)
rawLiftDown       := _IO_EM_DI_14;   (* spare: 4A has no sensors on this rig (CFG_LiftSensors) *)
rawLiftUp         := _IO_EM_DI_15;   (* spare *)

(* E-stop: NC contact means TRUE = healthy. *)
IF NOT CFG_EStopFitted THEN
    IN_EStopOk := TRUE;
ELSIF CFG_EStopNC THEN
    IN_EStopOk := rawEStop;
ELSE
    IN_EStopOk := NOT rawEStop;
END_IF;

(* Timed-move mode: a position counts as reached once its coil has been
   held for CFG_TravelTime. Uses last scan's outputs.
   Function blocks are called with positional arguments: TON(IN, PT). *)
tvFeedExt(OUT_FeedExtend, CFG_TravelTime);
tvFeedRet(OUT_FeedRetract, CFG_TravelTime);
tvUpperExt(OUT_UpperEjExtend, CFG_TravelTime);
tvUpperRet(OUT_UpperEjRetract, CFG_TravelTime);
tvLowerExt(OUT_LowerEjExtend, CFG_TravelTime);
tvLowerRet(OUT_LowerEjRetract, CFG_TravelTime);
tvLiftUp(OUT_LiftUp, CFG_TravelTime);
tvLiftDown(OUT_LiftDown, CFG_TravelTime);

IF CFG_UseReeds THEN
    IN_FeedExtended    := rawFeedExt;
    IN_FeedRetracted   := rawFeedRet;
    IN_UpperEjExtended := rawUpperExt;
    IN_UpperEjRetracted := rawUpperRet;
    IN_LowerEjExtended := rawLowerExt;
    IN_LowerEjRetracted := rawLowerRet;
ELSE
    IN_FeedExtended    := tvFeedExt.Q;
    IN_FeedRetracted   := tvFeedRet.Q;
    IN_UpperEjExtended := tvUpperExt.Q;
    IN_UpperEjRetracted := tvUpperRet.Q;
    IN_LowerEjExtended := tvLowerExt.Q;
    IN_LowerEjRetracted := tvLowerRet.Q;
END_IF;

(* Lift (4A) position without sensors: reached once its coil has been held
   for CFG_TravelTime, then remembered while both coils are off, because the
   bistable valve keeps the platform where it is. Energising a coil always
   re-times the move. *)
IF OUT_LiftUp THEN
    liftAtDown := FALSE;
    liftAtUp := tvLiftUp.Q;
ELSIF OUT_LiftDown THEN
    liftAtUp := FALSE;
    liftAtDown := tvLiftDown.Q;
END_IF;

IF CFG_UseReeds AND CFG_LiftSensors THEN
    IN_LiftUp   := rawLiftUp;
    IN_LiftDown := rawLiftDown;
ELSE
    IN_LiftUp   := liftAtUp;
    IN_LiftDown := liftAtDown;
END_IF;

(* Part on the lift platform: either material sensor sees it. *)
IN_PartOnLift := IN_Inductive OR IN_Capacitive;

(* Magazine: real sensor (BTS-01), or inferred from "fed but nothing arrived". *)
IF CFG_UseMagSensor THEN
    IN_MagazinePartPresent := rawMagPart;
ELSE
    IN_MagazinePartPresent := NOT magEmptyLatched;
END_IF;
tEmpty(NOT IN_MagazinePartPresent, CFG_EmptyDebounce);
IN_MagazineEmpty := tEmpty.Q;

(* ---------------------------------------------------------------------
   2. HOUSEKEEPING: edges, heartbeat, step timer
   --------------------------------------------------------------------- *)
rtStart(IN_StartPB OR HMI_Start);
rtStop(IN_StopPB OR HMI_Stop);
rtReset(IN_ResetPB OR HMI_Reset);
HMI_Start := FALSE;
HMI_Stop := FALSE;
HMI_Reset := FALSE;

(* Heartbeat: +1 every 100 ms so the twin can detect a stalled PLC. *)
tHb(NOT hbPulse, T#100ms);
hbPulse := tHb.Q;
IF hbPulse THEN
    HR_Heartbeat := HR_Heartbeat + 1;
    IF HR_Heartbeat > 30000 THEN
        HR_Heartbeat := 0;
    END_IF;
END_IF;

(* Step timer restarts whenever the step changes. *)
stepChanged := HR_Step <> stepPrev;
stepPrev := HR_Step;
tStep(NOT stepChanged, T#1h);
tCycle(cycleActive, T#1h);

HR_MapVersion := 1;

(* E-stop overrides everything. *)
IF NOT IN_EStopOk AND HR_Step <> 90 THEN
    HR_FaultCode := 1;
    HR_Step := 90;
END_IF;

(* Stop also counts during the homing that follows Start (wantAuto). *)
IF rtStop.Q AND (ST_AutoRunning OR wantAuto) THEN
    stopRequest := TRUE;
END_IF;

allRetracted := IN_FeedRetracted AND IN_UpperEjRetracted AND IN_LowerEjRetracted;

(* ---------------------------------------------------------------------
   3. SEQUENCE
   --------------------------------------------------------------------- *)
CASE HR_Step OF
    0: (* Idle: coils off, bistable valves hold position *)
        outEnable := FALSE;
        liftHold := FALSE;
        ST_AutoRunning := FALSE;
        stopRequest := FALSE;
        cycleActive := FALSE;
        IF rtStart.Q THEN
            magEmptyLatched := FALSE;
            wantAuto := TRUE;
            HR_Step := 1;
        ELSIF rtReset.Q THEN
            (* Reset in Idle: home the machine and clear counters. *)
            HR_PartsDone := 0;
            HR_MetalCount := 0;
            HR_PlasticCount := 0;
            HR_LastCycleMs := 0;
            magEmptyLatched := FALSE;
            wantAuto := FALSE;
            HR_Step := 1;
        END_IF;

    1: (* Homing: retract feed and ejectors first, then raise the lift *)
        outEnable := TRUE;
        cmdFeedExt := FALSE;
        cmdUpperExt := FALSE;
        cmdLowerExt := FALSE;
        cmdLiftUp := TRUE;
        liftHold := NOT allRetracted;
        IF allRetracted AND IN_LiftUp THEN
            liftHold := FALSE;
            HR_Material := 0;
            IF wantAuto AND NOT stopRequest THEN
                ST_AutoRunning := TRUE;
                HR_Step := 2;
            ELSE
                wantAuto := FALSE;
                stopRequest := FALSE;
                HR_Step := 0;
            END_IF;
        ELSIF tStep.ET >= CFG_HomeTimeout THEN
            HR_FaultCode := 9;
            HR_Step := 90;
        END_IF;

    2: (* WaitPart *)
        IF stopRequest THEN
            HR_Step := 13;
        ELSIF IN_PartOnLift AND allRetracted AND IN_LiftUp
              AND tStep.ET >= CFG_FeedDelay THEN
            (* A part is already on the platform (e.g. left by an E-stop):
               sort it before feeding another one onto it. *)
            cycleActive := TRUE;
            HR_Step := 5;
        ELSIF IN_MagazineEmpty AND tStep.ET >= CFG_EmptyDebounce THEN
            (* Batch complete. Waiting CFG_EmptyDebounce in this step stops a flag
               left over from the last batch from ending a run whose freshly loaded
               parts are still dropping to the sensor. *)
            HR_Step := 13;
        ELSIF IN_MagazinePartPresent AND NOT IN_PartOnLift AND allRetracted AND IN_LiftUp
              AND tStep.ET >= CFG_FeedDelay THEN
            cycleActive := TRUE;
            HR_Step := 3;
        END_IF;

    3: (* Feeding: 1A pushes the bottom part onto the lift *)
        cmdFeedExt := TRUE;
        IF IN_FeedExtended THEN
            HR_Step := 4;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 2;
            HR_Step := 90;
        END_IF;

    4: (* FeedRetract *)
        cmdFeedExt := FALSE;
        IF IN_FeedRetracted THEN
            HR_Step := 5;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 3;
            HR_Step := 90;
        END_IF;

    5: (* Detect: inductive = metal, capacitive only = plastic *)
        IF tStep.ET >= CFG_DetectSettle THEN
            IF IN_Inductive THEN
                HR_Material := 1;
                routeUpper := CFG_MetalToUpper;
                detected := TRUE;
            ELSIF IN_Capacitive THEN
                HR_Material := 2;
                routeUpper := NOT CFG_MetalToUpper;
                detected := TRUE;
            ELSE
                detected := FALSE;
            END_IF;

            IF detected THEN
                IF routeUpper THEN
                    HR_Step := 6;
                ELSE
                    HR_Step := 8;
                END_IF;
            ELSIF tStep.ET >= CFG_DetectWindow THEN
                cycleActive := FALSE;
                IF CFG_UseMagSensor THEN
                    HR_FaultCode := 4;
                    HR_Step := 90;
                ELSE
                    magEmptyLatched := TRUE;   (* nothing came out: magazine empty *)
                    HR_Step := 13;
                END_IF;
            END_IF;
        END_IF;

    6: (* UpperEject: 2A pushes the part off the raised platform *)
        cmdUpperExt := TRUE;
        IF IN_UpperEjExtended THEN
            HR_Step := 7;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 5;
            HR_Step := 90;
        END_IF;

    7: (* UpperRetract *)
        cmdUpperExt := FALSE;
        IF IN_UpperEjRetracted THEN
            HR_Step := 12;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 5;
            HR_Step := 90;
        END_IF;

    8: (* LiftDown: 4A lowers the platform to the lower route *)
        cmdLiftUp := FALSE;
        IF IN_LiftDown THEN
            HR_Step := 9;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 6;
            HR_Step := 90;
        END_IF;

    9: (* LowerEject: 3A pushes the part off the lowered platform *)
        cmdLowerExt := TRUE;
        IF IN_LowerEjExtended THEN
            HR_Step := 10;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 7;
            HR_Step := 90;
        END_IF;

    10: (* LowerRetract *)
        cmdLowerExt := FALSE;
        IF IN_LowerEjRetracted THEN
            HR_Step := 11;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 7;
            HR_Step := 90;
        END_IF;

    11: (* LiftUp *)
        cmdLiftUp := TRUE;
        IF IN_LiftUp THEN
            HR_Step := 12;
        ELSIF tStep.ET >= CFG_MoveTimeout THEN
            HR_FaultCode := 8;
            HR_Step := 90;
        END_IF;

    12: (* CycleDone: count and decide whether to continue *)
        HR_PartsDone := HR_PartsDone + 1;
        IF HR_Material = 1 THEN
            HR_MetalCount := HR_MetalCount + 1;
        ELSE
            HR_PlasticCount := HR_PlasticCount + 1;
        END_IF;
        cycleMs := ANY_TO_DINT(tCycle.ET);
        IF cycleMs > 32767 THEN
            cycleMs := 32767;
        END_IF;
        HR_LastCycleMs := ANY_TO_INT(cycleMs);
        cycleActive := FALSE;
        HR_Material := 0;
        IF stopRequest OR NOT IN_ContinuousMode THEN
            HR_Step := 13;
        ELSE
            HR_Step := 2;
        END_IF;

    13: (* Stopping: home, then Idle *)
        wantAuto := FALSE;
        stopRequest := FALSE;
        ST_AutoRunning := FALSE;
        HR_Step := 1;

    90: (* Fault: all coils off, wait for Reset with E-stop healthy *)
        outEnable := FALSE;
        liftHold := FALSE;
        ST_AutoRunning := FALSE;
        wantAuto := FALSE;
        stopRequest := FALSE;
        cycleActive := FALSE;
        IF rtReset.Q AND IN_EStopOk THEN
            HR_FaultCode := 0;
            HR_Material := 0;
            HR_Step := 1;
        END_IF;

ELSE
    HR_Step := 90;
END_CASE;

(* A timeout above sets step 90 mid-scan; drop the coils this scan, not next. *)
IF HR_Step = 90 THEN
    outEnable := FALSE;
END_IF;

(* ---------------------------------------------------------------------
   4. LOGICAL OUTPUTS (double-solenoid 5/2 valves: one coil per direction)
   --------------------------------------------------------------------- *)
OUT_FeedExtend     := outEnable AND cmdFeedExt;
OUT_FeedRetract    := outEnable AND NOT cmdFeedExt;
OUT_UpperEjExtend  := outEnable AND cmdUpperExt;
OUT_UpperEjRetract := outEnable AND NOT cmdUpperExt;
OUT_LowerEjExtend  := outEnable AND cmdLowerExt;
OUT_LowerEjRetract := outEnable AND NOT cmdLowerExt;
OUT_LiftUp         := outEnable AND NOT liftHold AND cmdLiftUp;
OUT_LiftDown       := outEnable AND NOT liftHold AND NOT cmdLiftUp;

ST_Faulted := HR_Step = 90;
ST_Homed := allRetracted AND IN_LiftUp;

(* ---------------------------------------------------------------------
   5. LOGICAL OUTPUTS -> PHYSICAL OUTPUTS
      Patch DO-n -> SOL-xx as listed; PP-xx tubing as in Kit1_IO_Allocation.csv.
      If you patch differently, change ONLY this section.
   --------------------------------------------------------------------- *)
_IO_EM_DO_00 := OUT_FeedExtend;      (* -> SOL-01 (1S1) -> PP-01 -> 1A extend  (checked at the rig) *)
_IO_EM_DO_01 := OUT_FeedRetract;     (* -> SOL-02 (1S2) -> PP-02 -> 1A retract *)
_IO_EM_DO_02 := OUT_UpperEjExtend;   (* -> SOL-03 (2S1) -> PP-03 -> 2A extend  (checked at the rig) *)
_IO_EM_DO_03 := OUT_UpperEjRetract;  (* -> SOL-04 (2S2) -> PP-04 -> 2A retract *)
_IO_EM_DO_04 := OUT_LowerEjRetract;  (* -> SOL-05 (3S1) -> PP-05 -> 3A retract (checked at the rig: 3A is the other way round) *)
_IO_EM_DO_05 := OUT_LowerEjExtend;   (* -> SOL-06 (3S2) -> PP-06 -> 3A extend  *)
_IO_EM_DO_06 := OUT_LiftUp;          (* -> SOL-07 (4S1) -> PP-07 -> 4A platform up (confirmed at the rig) *)
_IO_EM_DO_07 := OUT_LiftDown;        (* -> SOL-08 (4S2) -> PP-08 -> 4A platform down *)
