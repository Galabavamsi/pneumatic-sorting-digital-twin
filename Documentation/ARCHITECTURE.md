# Architecture

## Design goals

- Keep the mechanical simulation usable without PLC hardware.
- Separate logical PLC tags from future controller-specific addresses.
- Keep HMI code independent from kit-specific mechanism details through adapters.
- Make unsafe or stale external control fail toward known outputs.
- Support additional training kits without copying the entire UI stack.

## Runtime layers

### Scene and CAD inspection layer

Each kit has its own scene and station root. `KitSceneContext` prevents Kit 1-specific control services from starting in another kit's scene. `KitComponentRegistry` and `ComponentIdentifier` provide shared component discovery, selection, focus, and isolation for segmented CAD assemblies.

### Mechanical layer

`PneumaticCylinderTest`, `SecondCylinderTest`, `ThirdCylinderTest`, and `LiftCylinderTest` own calibrated axis motion. `MagazineFeedTest` transfers the active workpiece between the magazine, lift, ejector, and gravity simulation. `Kit1MagazineBatch` manages the remaining stack and randomized materials.

Kit 2 uses `Kit2FeedCylinderCalibration`, `Kit2SecondCylinderCalibration`, and `Kit2StampingCylinderCalibration` for its four axes. `Kit2MagazineFeedCalibration` manages a stable 16-piece stored stack, converts released pieces into independent rigid bodies, and preserves FIFO behavior across both stamping stations and discharge ramps.

### Sensor and PLC abstraction

`Kit1SensorSimulation` derives digital sensor states from the simulated mechanism. `Kit1PlcIo` exposes controller-neutral `DI_*` and `DO_*` properties and applies output commands to actuators.

`Kit2PlcIo` provides the same boundary for magazine presence, material classification, station occupancy, axis positions, stamp positions, utilities, and actuator commands. Sensor-failure injection masks the logical input rather than changing the workpiece's physical state.

Physical PLC addresses do not belong in mechanism scripts. A future transport adapter should map controller tags to this logical layer.

### Sequence layer

`Kit1AutomaticSequence` implements the offline sorting state machine. The same logical I/O contract can later be driven by a real PLC, with Unity operating as the plant model and visualization layer.

`Kit2AutomaticSequence` implements continuous, stepped, paused, and safe-stop stamping cycles. The default recipe routes metal through Stamp A to Bin 1 and plastic through Stamp B to Bin 2.

### Electropneumatic utilities

`ElectropneumaticUtilities` provides a lightweight supply model:

- binary 24 VDC control power;
- configurable regulated pressure;
- minimum-pressure interlock and pressure-dependent axis speed;
- E-stop state;
- air-leak, stuck-actuator, and failed-sensor injection.

`Kit2ElectropneumaticUtilities` applies the equivalent utility and per-axis fault model to Kit 2. The current 6 bar nominal and 3.5 bar minimum values are simulation defaults, not confirmed hardware ratings.

### HMI and monitoring

`KitHmiAdapterBase` is the shared HMI contract. `Kit1HmiAdapter` binds that contract to Kit 1. `UniversalKitHmi` supplies the operator view, while `Kit1EngineeringDashboard` supplies commissioning and diagnostics.

`ProductionMonitor` stores semantic events and production KPIs. `DigitalTwinHistorian` samples the tag and actuator timeline at 10 Hz, exports CSV, and replays recorded actuator motion locally.

`Kit2EngineeringDashboard` presents Kit 2 state, production counters, live logical I/O, utility controls, fault injection, and diagnostics. It shares `ProductionMonitor` for semantic cycle events; Kit 1's actuator historian remains kit-specific.

## Future PLC transport boundary

A real transport should be added behind a controller-neutral interface with:

- connect/disconnect lifecycle;
- tag-quality and timestamp metadata;
- heartbeat and stale-data detection;
- explicit simulation/read-only/live-write modes;
- command acknowledgement and timeout handling;
- safe output fallback on disconnect.

Select EtherNet/IP, OPC UA, Modbus TCP, or another driver only after the controller and available interfaces are confirmed.
