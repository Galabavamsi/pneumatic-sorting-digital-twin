# Architecture

## Design goals

- Keep the mechanical simulation usable without PLC hardware.
- Separate logical PLC tags from future controller-specific addresses.
- Keep HMI code independent from kit-specific mechanism details through adapters.
- Make unsafe or stale external control fail toward known outputs.
- Support additional training kits without copying the entire UI stack.

## Runtime layers

### Mechanical layer

`PneumaticCylinderTest`, `SecondCylinderTest`, `ThirdCylinderTest`, and `LiftCylinderTest` own calibrated axis motion. `MagazineFeedTest` transfers the active workpiece between the magazine, lift, ejector, and gravity simulation. `Kit1MagazineBatch` manages the remaining stack and randomized materials.

### Sensor and PLC abstraction

`Kit1SensorSimulation` derives digital sensor states from the simulated mechanism. `Kit1PlcIo` exposes controller-neutral `DI_*` and `DO_*` properties and applies output commands to actuators.

Physical PLC addresses do not belong in mechanism scripts. A future transport adapter should map controller tags to this logical layer.

### Sequence layer

`Kit1AutomaticSequence` implements the offline sorting state machine. The same logical I/O contract can later be driven by a real PLC, with Unity operating as the plant model and visualization layer.

### Electropneumatic utilities

`ElectropneumaticUtilities` provides a lightweight supply model:

- binary 24 VDC control power;
- configurable regulated pressure;
- minimum-pressure interlock and pressure-dependent axis speed;
- E-stop state;
- air-leak, stuck-actuator, and failed-sensor injection.

The current 6 bar nominal and 3.5 bar minimum values are simulation defaults, not confirmed hardware ratings.

### HMI and monitoring

`KitHmiAdapterBase` is the shared HMI contract. `Kit1HmiAdapter` binds that contract to Kit 1. `UniversalKitHmi` supplies the operator view, while `Kit1EngineeringDashboard` supplies commissioning and diagnostics.

`ProductionMonitor` stores semantic events and production KPIs. `DigitalTwinHistorian` samples the tag and actuator timeline at 10 Hz, exports CSV, and replays recorded actuator motion locally.

## Future PLC transport boundary

A real transport should be added behind a controller-neutral interface with:

- connect/disconnect lifecycle;
- tag-quality and timestamp metadata;
- heartbeat and stale-data detection;
- explicit simulation/read-only/live-write modes;
- command acknowledgement and timeout handling;
- safe output fallback on disconnect.

Select EtherNet/IP, OPC UA, Modbus TCP, or another driver only after the controller and available interfaces are confirmed.
