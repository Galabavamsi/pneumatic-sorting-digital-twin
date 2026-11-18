(* =====================================================================
   Kit 2 two-station stamping and sorting  -  Micro850 (2080-L50E-48QBB), CCW
   Program POU name: Kit2_Sort   (Structured Text)

   Variables: create them first from PLC/Kit2/Kit2_Variables.csv
   ("Global" rows in Global Variables, "Local" rows in this program).

   What the machine does:
     1A pushes the bottom job from the magazine to Stamp 1. The inductive
     sensor IS-01 there sees metal.
       metal   : 3A stamps it at Stamp 1. It stays there; the NEXT job fed
                 by 1A pushes it on into Tray 1.
       plastic : 2A pushes it across to Stamp 2, where CS-01 sees it, and
                 4A stamps it. It stays there; the NEXT plastic job pushed
                 by 2A pushes it on into Tray 2.
     So the last job of each kind stays on the machine at the end of a batch.

   All four cylinders have a reed switch at each end, so every move waits
   for its switch (with a timeout) instead of a fixed time.

   Steps (K2_Step):
     0 Idle        1 Home
    10 WaitPart   11 Feed           12 FeedRetract   13 Detect
    20 StampADown 21 StampAUp
    30 Transfer   31 TransferRetract 32 CheckAtB
    33 StampBDown 34 StampBUp
    40 CycleDone  90 Fault
   Fault codes (K2_FaultCode):
     1 E-stop                 2 feed did not extend    3 feed did not retract
     4 job not seen at Stamp 2                         5 transfer did not extend
     6 transfer did not retract                        7 Stamp 1 timeout
     8 Stamp 2 timeout        9 homing did not finish
   ===================================================================== *)

(* ---------------------------------------------------------------------
   TUNING: change the values here, then Build and Download.
   These lines override the initial values in the variable table.
   --------------------------------------------------------------------- *)
K2_CFG_S1IsRetracted := TRUE;      (* TRUE: the "S1" switch (DI-0, 2, 4, 6) is the pulled-in end.
                                      With everything pulled in, lights 0, 2, 4, 6 should be on.
                                      If lights 1, 3, 5, 7 are on instead, set this to FALSE. *)
K2_T_MoveTimeout  := T#4s;         (* longest allowed cylinder move *)
K2_T_HomeTimeout  := T#8s;         (* longest allowed homing *)
K2_T_DetectSettle := T#500ms;      (* wait before reading IS-01 *)
K2_T_SeenAtB      := T#1500ms;     (* CS-01 must see the job at Stamp 2 within this time *)
K2_T_StampDwell   := T#500ms;      (* stamp stays down this long *)
K2_T_FeedDelay    := T#500ms;      (* pause before each feed *)
K2_T_EmptyDebounce := T#2s;        (* magazine empty this long ends the batch *)

(* ---------------------------------------------------------------------
   1. PHYSICAL INPUTS -> LOGICAL INPUTS
      If you patch differently, change ONLY this section.
   --------------------------------------------------------------------- *)
IF K2_CFG_S1IsRetracted THEN
    K2_IN_FeedRet   := _IO_EM_DI_00;   K2_IN_FeedExt   := _IO_EM_DI_01;   (* 1A: 1S1, 1S2 *)
    K2_IN_XferRet   := _IO_EM_DI_02;   K2_IN_XferExt   := _IO_EM_DI_03;   (* 2A: 2S1, 2S2 *)
    K2_IN_StampAUp  := _IO_EM_DI_04;   K2_IN_StampADn  := _IO_EM_DI_05;   (* 3A: 3S1, 3S2 *)
    K2_IN_StampBUp  := _IO_EM_DI_06;   K2_IN_StampBDn  := _IO_EM_DI_07;   (* 4A: 4S1, 4S2 *)
ELSE
    K2_IN_FeedRet   := _IO_EM_DI_01;   K2_IN_FeedExt   := _IO_EM_DI_00;
    K2_IN_XferRet   := _IO_EM_DI_03;   K2_IN_XferExt   := _IO_EM_DI_02;
    K2_IN_StampAUp  := _IO_EM_DI_05;   K2_IN_StampADn  := _IO_EM_DI_04;
    K2_IN_StampBUp  := _IO_EM_DI_07;   K2_IN_StampBDn  := _IO_EM_DI_06;
END_IF;
K2_IN_Inductive  := _IO_EM_DI_08;   (* IS-01 at Stamp 1: metal *)
K2_IN_Capacitive := _IO_EM_DI_09;   (* CS-01 at Stamp 2: any job *)
K2_IN_MagPart    := _IO_EM_DI_10;   (* magazine sensor: a job is waiting *)
K2_IN_ResetPB    := _IO_EM_DI_11;   (* black  *)
rawEStop         := _IO_EM_DI_12;   (* E-stop contact *)
K2_IN_Continuous := _IO_EM_DI_13;   (* selector: ON = run until the magazine is empty *)
K2_IN_StartPB    := _IO_EM_DI_14;   (* green  *)
K2_IN_StopPB     := _IO_EM_DI_15;   (* red    *)

IF NOT K2_CFG_EStopFitted THEN
    K2_IN_EStopOk := TRUE;
ELSIF K2_CFG_EStopNC THEN
    K2_IN_EStopOk := rawEStop;
ELSE
    K2_IN_EStopOk := NOT rawEStop;
END_IF;

(* ---------------------------------------------------------------------
   2. HOUSEKEEPING
   --------------------------------------------------------------------- *)
rtStart(K2_IN_StartPB OR K2_HMI_Start);
rtStop(K2_IN_StopPB OR K2_HMI_Stop);
rtReset(K2_IN_ResetPB OR K2_HMI_Reset);
K2_HMI_Start := FALSE;
K2_HMI_Stop := FALSE;
K2_HMI_Reset := FALSE;

tHb(NOT hbPulse, T#100ms);
hbPulse := tHb.Q;
IF hbPulse THEN
    K2_Heartbeat := K2_Heartbeat + 1;
    IF K2_Heartbeat > 30000 THEN
        K2_Heartbeat := 0;
    END_IF;
END_IF;

(* Step timer restarts whenever the step changes. *)
stepChanged := K2_Step <> stepPrev;
stepPrev := K2_Step;
tStep(NOT stepChanged, T#1h);

(* Stamp hold timer: runs while a stamp is down in its stamping step.
   CCW allows only one way of calling a function block instance, so it is called here once. *)
tDwell((K2_Step = 20 AND K2_IN_StampADn) OR (K2_Step = 33 AND K2_IN_StampBDn), K2_T_StampDwell);

IF NOT K2_IN_EStopOk AND K2_Step <> 90 THEN
    K2_FaultCode := 1;
    K2_Step := 90;
END_IF;

IF rtStop.Q AND (K2_ST_AutoRunning OR wantAuto) THEN
    stopRequest := TRUE;
END_IF;

(* ---------------------------------------------------------------------
   3. SEQUENCE
   --------------------------------------------------------------------- *)
CASE K2_Step OF
    0: (* Idle: all coils off; the valves keep their place *)
        outEnable := FALSE;
        K2_ST_AutoRunning := FALSE;
        stopRequest := FALSE;
        IF rtStart.Q THEN
            wantAuto := TRUE;
            K2_Step := 1;
        ELSIF rtReset.Q THEN
            K2_PartsDone := 0;
            K2_MetalCount := 0;
            K2_PlasticCount := 0;
            wantAuto := FALSE;
            K2_Step := 1;
        END_IF;

    1: (* Home: both stamps up first, then both pushers in *)
        outEnable := TRUE;
        K2_OUT_StampADown := FALSE;
        K2_OUT_StampBDown := FALSE;
        IF K2_IN_StampAUp AND K2_IN_StampBUp THEN
            K2_OUT_FeedExt := FALSE;
            K2_OUT_XferExt := FALSE;
        END_IF;
        IF K2_IN_StampAUp AND K2_IN_StampBUp AND K2_IN_FeedRet AND K2_IN_XferRet AND tStep.ET >= T#500ms THEN
            K2_Material := 0;
            IF wantAuto AND NOT stopRequest THEN
                K2_ST_AutoRunning := TRUE;
                K2_Step := 10;
            ELSE
                wantAuto := FALSE;
                stopRequest := FALSE;
                K2_Step := 0;
            END_IF;
        ELSIF tStep.ET >= K2_T_HomeTimeout THEN
            K2_FaultCode := 9;
            K2_Step := 90;
        END_IF;

    10: (* Wait for a job in the magazine *)
        IF stopRequest THEN
            wantAuto := FALSE;
            K2_Step := 0;
        ELSIF K2_IN_MagPart AND tStep.ET >= K2_T_FeedDelay THEN
            K2_Step := 11;
        ELSIF NOT K2_IN_MagPart AND tStep.ET >= K2_T_EmptyDebounce THEN
            wantAuto := FALSE;          (* magazine empty: batch finished *)
            K2_Step := 0;
        END_IF;

    11: (* Feed: 1A pushes the job to Stamp 1 (and the job before it into Tray 1) *)
        K2_OUT_FeedExt := TRUE;
        IF K2_IN_FeedExt THEN
            K2_Step := 12;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 2;
            K2_Step := 90;
        END_IF;

    12: (* Feed back *)
        K2_OUT_FeedExt := FALSE;
        IF K2_IN_FeedRet THEN
            K2_Step := 13;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 3;
            K2_Step := 90;
        END_IF;

    13: (* Detect at Stamp 1: 1 = metal, 2 = plastic *)
        IF tStep.ET >= K2_T_DetectSettle THEN
            IF K2_IN_Inductive THEN
                K2_Material := 1;
                K2_Step := 20;
            ELSE
                K2_Material := 2;
                K2_Step := 30;
            END_IF;
        END_IF;

    20: (* Metal: Stamp 1 down, hold *)
        K2_OUT_StampADown := TRUE;
        IF K2_IN_StampADn AND tDwell.Q THEN
            K2_Step := 21;
        ELSIF NOT K2_IN_StampADn AND tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 7;
            K2_Step := 90;
        END_IF;

    21: (* Stamp 1 up *)
        K2_OUT_StampADown := FALSE;
        IF K2_IN_StampAUp THEN
            K2_Step := 40;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 7;
            K2_Step := 90;
        END_IF;

    30: (* Plastic: 2A pushes it across to Stamp 2 (and the job before it into Tray 2) *)
        K2_OUT_XferExt := TRUE;
        IF K2_IN_XferExt THEN
            K2_Step := 31;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 5;
            K2_Step := 90;
        END_IF;

    31: (* Transfer back *)
        K2_OUT_XferExt := FALSE;
        IF K2_IN_XferRet THEN
            K2_Step := 32;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 6;
            K2_Step := 90;
        END_IF;

    32: (* CS-01 must see the job at Stamp 2 *)
        IF K2_IN_Capacitive THEN
            K2_Step := 33;
        ELSIF tStep.ET >= K2_T_SeenAtB THEN
            K2_FaultCode := 4;
            K2_Step := 90;
        END_IF;

    33: (* Stamp 2 down, hold *)
        K2_OUT_StampBDown := TRUE;
        IF K2_IN_StampBDn AND tDwell.Q THEN
            K2_Step := 34;
        ELSIF NOT K2_IN_StampBDn AND tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 8;
            K2_Step := 90;
        END_IF;

    34: (* Stamp 2 up *)
        K2_OUT_StampBDown := FALSE;
        IF K2_IN_StampBUp THEN
            K2_Step := 40;
        ELSIF tStep.ET >= K2_T_MoveTimeout THEN
            K2_FaultCode := 8;
            K2_Step := 90;
        END_IF;

    40: (* Count, then next job or stop *)
        K2_PartsDone := K2_PartsDone + 1;
        IF K2_Material = 1 THEN
            K2_MetalCount := K2_MetalCount + 1;
        ELSE
            K2_PlasticCount := K2_PlasticCount + 1;
        END_IF;
        K2_Material := 0;
        IF stopRequest OR NOT K2_IN_Continuous THEN
            wantAuto := FALSE;
            stopRequest := FALSE;
            K2_Step := 0;
        ELSE
            K2_Step := 10;
        END_IF;

    90: (* Fault: all coils off, wait for Reset *)
        outEnable := FALSE;
        K2_ST_AutoRunning := FALSE;
        wantAuto := FALSE;
        stopRequest := FALSE;
        IF rtReset.Q AND K2_IN_EStopOk THEN
            K2_FaultCode := 0;
            K2_Material := 0;
            K2_Step := 1;
        END_IF;

ELSE
    K2_Step := 90;
END_CASE;

IF K2_Step = 90 THEN
    outEnable := FALSE;
END_IF;

K2_ST_Faulted := K2_Step = 90;

(* ---------------------------------------------------------------------
   4. PHYSICAL OUTPUTS: one coil per direction, never both
      DO-0..DO-7 -> SOL-01..SOL-08, as on the wiring notes.
      If a cylinder moves the wrong way, swap its two lines here.
   --------------------------------------------------------------------- *)
_IO_EM_DO_00 := outEnable AND K2_OUT_FeedExt;          (* SOL-01  1A extend  *)
_IO_EM_DO_01 := outEnable AND NOT K2_OUT_FeedExt;      (* SOL-02  1A retract *)
_IO_EM_DO_02 := outEnable AND K2_OUT_XferExt;          (* SOL-03  2A extend  *)
_IO_EM_DO_03 := outEnable AND NOT K2_OUT_XferExt;      (* SOL-04  2A retract *)
_IO_EM_DO_04 := outEnable AND K2_OUT_StampADown;       (* SOL-05  3A extend = Stamp 1 down *)
_IO_EM_DO_05 := outEnable AND NOT K2_OUT_StampADown;   (* SOL-06  3A retract *)
_IO_EM_DO_06 := outEnable AND K2_OUT_StampBDown;       (* SOL-07  4A extend = Stamp 2 down *)
_IO_EM_DO_07 := outEnable AND NOT K2_OUT_StampBDown;   (* SOL-08  4A retract *)
