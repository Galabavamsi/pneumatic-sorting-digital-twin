(* =====================================================================
   Kit 3 stamping and vacuum sorting  -  Micro850 (2080-L50E-48QBB), CCW
   Program POU name: Kit3_Sort   (Structured Text)

   Variables: create them first from PLC/Kit3/Kit3_Variables.csv
   ("Global" rows in Global Variables, "Local" rows in this program).

   Only the feed cylinder 1A has position switches. The stamp (2A), the
   vacuum slide (3A), the arm lift (4A) and the rotary (5A) are timed.

   Home: 1A in, stamp up, arm up, slide in, rotary on the Tray 1 side,
   vacuum off.

   Steps (K3_Step):
     0 Idle        1 Home: arm up   2 Home: slide in  3 Home: rotary
    10 WaitPart   11 Feed          12 FeedRetract    13 ReadCamera
    14 StampDown  15 StampUp       16 SlideOut       17 ArmDown
    18 VacuumOn   19 ArmUp         20 SlideIn        21 RotateToTray2
    22 SlideOutAtTray2             23 Release        24 SlideInAfterDrop
    25 RotateBack 26 CycleDone     90 Fault
   Fault codes (K3_FaultCode):
     1 E-stop   2 feed did not extend   3 feed did not retract   9 homing
   ===================================================================== *)

(* ---------------------------------------------------------------------
   1. PHYSICAL INPUTS -> LOGICAL INPUTS
      If you patch differently, change ONLY this section.
   --------------------------------------------------------------------- *)
K3_IN_FeedRetracted := _IO_EM_DI_00;   (* 1A reed, pulled in  *)
K3_IN_FeedExtended  := _IO_EM_DI_01;   (* 1A reed, pushed out *)
K3_IN_MagPart       := _IO_EM_DI_02;   (* part waiting in the magazine *)
K3_IN_Camera        := _IO_EM_DI_03;   (* camera / colour sensor output *)
K3_IN_StartPB       := _IO_EM_DI_09;   (* green  *)
K3_IN_StopPB        := _IO_EM_DI_10;   (* red    *)
K3_IN_ResetPB       := _IO_EM_DI_11;   (* black  *)
rawEStop            := _IO_EM_DI_12;   (* E-stop contact *)
K3_IN_Continuous    := _IO_EM_DI_13;   (* selector: ON = run until the magazine is empty *)

IF NOT K3_CFG_EStopFitted THEN
    K3_IN_EStopOk := TRUE;
ELSIF K3_CFG_EStopNC THEN
    K3_IN_EStopOk := rawEStop;
ELSE
    K3_IN_EStopOk := NOT rawEStop;
END_IF;

(* ---------------------------------------------------------------------
   TUNING: change the values here, then Build and Download.
   These lines override the initial values in the variable table.
   --------------------------------------------------------------------- *)
K3_CFG_CameraOnIsMetal := TRUE;     (* camera output ON = metal (bright), OFF = black plastic *)
K3_T_CameraSettle := T#500ms;       (* wait before reading the camera *)
K3_T_Stamp   := T#3s;               (* stamp travel, each way *)
K3_T_Slide   := T#3s;               (* vacuum head out / in (3A) *)
K3_T_Lift    := T#3s;               (* arm down / up (4A) *)
K3_T_Rotary  := T#3s;               (* arm turn (5A) *)
K3_T_Vacuum  := T#3s;               (* grip time, head pressed on the part, before lifting *)
K3_T_Release := T#2s;               (* drop time before moving *)

(* ---------------------------------------------------------------------
   2. HOUSEKEEPING
   --------------------------------------------------------------------- *)
rtStart(K3_IN_StartPB OR K3_HMI_Start);
rtStop(K3_IN_StopPB OR K3_HMI_Stop);
rtReset(K3_IN_ResetPB OR K3_HMI_Reset);
K3_HMI_Start := FALSE;
K3_HMI_Stop := FALSE;
K3_HMI_Reset := FALSE;

tHb(NOT hbPulse, T#100ms);
hbPulse := tHb.Q;
IF hbPulse THEN
    K3_Heartbeat := K3_Heartbeat + 1;
    IF K3_Heartbeat > 30000 THEN
        K3_Heartbeat := 0;
    END_IF;
END_IF;

(* Step timer restarts whenever the step changes. *)
stepChanged := K3_Step <> stepPrev;
stepPrev := K3_Step;
tStep(NOT stepChanged, T#1h);

IF NOT K3_IN_EStopOk AND K3_Step <> 90 THEN
    K3_FaultCode := 1;
    K3_Step := 90;
END_IF;

IF rtStop.Q AND (K3_ST_AutoRunning OR wantAuto) THEN
    stopRequest := TRUE;
END_IF;

(* ---------------------------------------------------------------------
   3. SEQUENCE
   --------------------------------------------------------------------- *)
CASE K3_Step OF
    0: (* Idle. Once homed, the home coils stay on, so the head stays pulled in and
          the arm stays on the Tray 1 side even when the air is switched on later.
          The head must never be out while the arm turns: it catches the stamp frame. *)
        outEnable := homed;
        K3_ST_AutoRunning := FALSE;
        stopRequest := FALSE;
        IF rtStart.Q THEN
            wantAuto := TRUE;
            K3_Step := 1;
        ELSIF rtReset.Q THEN
            K3_PartsDone := 0;
            K3_MetalCount := 0;
            K3_PlasticCount := 0;
            wantAuto := FALSE;
            K3_Step := 1;
        END_IF;

    1: (* Home, part 1: feed in, stamp up, vacuum off, arm up *)
        outEnable := TRUE;
        K3_OUT_FeedExt := FALSE;
        K3_OUT_StampDown := FALSE;
        K3_OUT_Vacuum := FALSE;
        K3_OUT_LiftUp := TRUE;
        rotEnable := FALSE;             (* do not turn the arm until the head is in *)
        IF K3_IN_FeedRetracted AND tStep.ET >= K3_T_Lift THEN
            K3_Step := 2;
        ELSIF tStep.ET >= K3_T_HomeTimeout THEN
            K3_FaultCode := 9;
            K3_Step := 90;
        END_IF;

    2: (* Home, part 2: slide in, only once the arm is up *)
        K3_OUT_SlideExt := FALSE;
        IF tStep.ET >= K3_T_Slide THEN
            K3_Step := 3;
        END_IF;

    3: (* Home, part 3: rotary to the Tray 1 side *)
        K3_OUT_RotTray2 := FALSE;
        rotEnable := TRUE;
        IF tStep.ET >= K3_T_Rotary THEN
            K3_Material := 0;
            homed := TRUE;
            IF wantAuto AND NOT stopRequest THEN
                K3_ST_AutoRunning := TRUE;
                K3_Step := 10;
            ELSE
                wantAuto := FALSE;
                stopRequest := FALSE;
                K3_Step := 0;
            END_IF;
        END_IF;

    10: (* Wait for a part in the magazine *)
        IF stopRequest THEN
            wantAuto := FALSE;
            K3_Step := 0;
        ELSIF K3_IN_MagPart AND tStep.ET >= K3_T_FeedDelay THEN
            K3_Step := 11;
        ELSIF NOT K3_IN_MagPart AND tStep.ET >= K3_T_EmptyDebounce THEN
            wantAuto := FALSE;          (* magazine empty: batch finished *)
            K3_Step := 0;
        END_IF;

    11: (* Feed: 1A pushes the bottom part to the stamp position *)
        K3_OUT_FeedExt := TRUE;
        IF K3_IN_FeedExtended THEN
            K3_Step := 12;
        ELSIF tStep.ET >= K3_T_MoveTimeout THEN
            K3_FaultCode := 2;
            K3_Step := 90;
        END_IF;

    12: (* Feed back *)
        K3_OUT_FeedExt := FALSE;
        IF K3_IN_FeedRetracted THEN
            K3_Step := 13;
        ELSIF tStep.ET >= K3_T_MoveTimeout THEN
            K3_FaultCode := 3;
            K3_Step := 90;
        END_IF;

    13: (* Read the camera: 1 = metal, 2 = plastic *)
        IF tStep.ET >= K3_T_CameraSettle THEN
            IF K3_IN_Camera = K3_CFG_CameraOnIsMetal THEN
                K3_Material := 1;
            ELSE
                K3_Material := 2;
            END_IF;
            K3_Step := 14;
        END_IF;

    14: (* Stamp down *)
        K3_OUT_StampDown := TRUE;
        IF tStep.ET >= K3_T_Stamp THEN
            K3_Step := 15;
        END_IF;

    15: (* Stamp up *)
        K3_OUT_StampDown := FALSE;
        IF tStep.ET >= K3_T_Stamp THEN
            K3_Step := 16;
        END_IF;

    16: (* Vacuum head out over the part (arm is up) *)
        K3_OUT_SlideExt := TRUE;
        IF tStep.ET >= K3_T_Slide THEN
            K3_Step := 17;
        END_IF;

    17: (* Arm down onto the part *)
        K3_OUT_LiftUp := FALSE;
        IF tStep.ET >= K3_T_Lift THEN
            K3_Step := 18;
        END_IF;

    18: (* Vacuum on *)
        K3_OUT_Vacuum := TRUE;
        IF tStep.ET >= K3_T_Vacuum THEN
            K3_Step := 19;
        END_IF;

    19: (* Arm up with the part. The head stays out and nothing else moves.
           6 s on purpose: on this rig the arm lifts slowly with the head out. *)
        K3_OUT_LiftUp := TRUE;
        IF tStep.ET >= T#6s THEN
            K3_Step := 20;
        END_IF;

    20: (* Head back: now over Tray 1 *)
        K3_OUT_SlideExt := FALSE;
        IF tStep.ET >= K3_T_Slide THEN
            IF K3_Material = 1 THEN
                K3_Step := 21;          (* metal: on to Tray 2 *)
            ELSE
                K3_Step := 23;          (* plastic: drop into Tray 1 *)
            END_IF;
        END_IF;

    21: (* Turn to Tray 2 *)
        K3_OUT_RotTray2 := TRUE;
        IF tStep.ET >= K3_T_Rotary THEN
            IF K3_CFG_Tray2Extended THEN
                K3_Step := 22;
            ELSE
                K3_Step := 23;
            END_IF;
        END_IF;

    22: (* Only if Tray 2 is reached with the head out *)
        K3_OUT_SlideExt := TRUE;
        IF tStep.ET >= K3_T_Slide THEN
            K3_Step := 23;
        END_IF;

    23: (* Release the part *)
        K3_OUT_Vacuum := FALSE;
        IF tStep.ET >= K3_T_Release THEN
            K3_Step := 24;
        END_IF;

    24: (* Head back in, if it was out at Tray 2 *)
        IF K3_OUT_SlideExt THEN
            K3_OUT_SlideExt := FALSE;
        ELSIF K3_Material <> 1 OR NOT K3_CFG_Tray2Extended OR tStep.ET >= K3_T_Slide THEN
            K3_Step := 25;
        END_IF;

    25: (* Turn back to the Tray 1 side *)
        K3_OUT_RotTray2 := FALSE;
        IF K3_Material <> 1 OR tStep.ET >= K3_T_Rotary THEN
            K3_Step := 26;
        END_IF;

    26: (* Count, then next part or stop *)
        K3_PartsDone := K3_PartsDone + 1;
        IF K3_Material = 1 THEN
            K3_MetalCount := K3_MetalCount + 1;
        ELSE
            K3_PlasticCount := K3_PlasticCount + 1;
        END_IF;
        K3_Material := 0;
        IF stopRequest OR NOT K3_IN_Continuous THEN
            wantAuto := FALSE;
            stopRequest := FALSE;
            K3_Step := 0;
        ELSE
            K3_Step := 10;
        END_IF;

    90: (* Fault: all coils off (a held part is dropped), wait for Reset *)
        outEnable := FALSE;
        homed := FALSE;
        rotEnable := FALSE;
        K3_ST_AutoRunning := FALSE;
        wantAuto := FALSE;
        stopRequest := FALSE;
        IF rtReset.Q AND K3_IN_EStopOk THEN
            K3_FaultCode := 0;
            K3_Material := 0;
            K3_Step := 1;
        END_IF;

ELSE
    K3_Step := 90;
END_CASE;

IF K3_Step = 90 THEN
    outEnable := FALSE;
END_IF;

K3_ST_Faulted := K3_Step = 90;

(* ---------------------------------------------------------------------
   4. PHYSICAL OUTPUTS: one coil per direction, never both
      DO-0..DO-9 -> SOL-01..SOL-10, DO-10 -> VC (vacuum).
      If you patch differently, change ONLY this section.
   --------------------------------------------------------------------- *)
_IO_EM_DO_00 := outEnable AND K3_OUT_FeedExt;         (* SOL-01  1A out        *)
_IO_EM_DO_01 := outEnable AND NOT K3_OUT_FeedExt;     (* SOL-02  1A in         *)
_IO_EM_DO_02 := outEnable AND K3_OUT_StampDown;       (* SOL-03  stamp down    *)
_IO_EM_DO_03 := outEnable AND NOT K3_OUT_StampDown;   (* SOL-04  stamp up      *)
(* Checked on the rig on 2026-11-15 against a recording of the outputs:
   DO-4/DO-5 are the two coils of the 3A valve, DO-6/DO-7 the two coils of the 4A valve.
   Do not pair them any other way: two coils of one valve on together = no move. *)
_IO_EM_DO_04 := outEnable AND NOT K3_OUT_SlideExt;    (* SOL-05  head in  (3A pipes were swapped on 2026-11-15) *)
_IO_EM_DO_05 := outEnable AND K3_OUT_SlideExt;        (* SOL-06  head out *)
_IO_EM_DO_06 := outEnable AND K3_OUT_LiftUp;          (* SOL-07  arm up        *)
_IO_EM_DO_07 := outEnable AND NOT K3_OUT_LiftUp;      (* SOL-08  arm down      *)
_IO_EM_DO_08 := outEnable AND rotEnable AND NOT K3_OUT_RotTray2;   (* SOL-09  Tray 1 side, held on: 5A sits in between when no coil is on *)
_IO_EM_DO_09 := outEnable AND rotEnable AND K3_OUT_RotTray2;       (* SOL-10  Tray 2 *)
_IO_EM_DO_10 := outEnable AND K3_OUT_Vacuum;          (* VC      vacuum on     *)
