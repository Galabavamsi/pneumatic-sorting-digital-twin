# Contributing

Thank you for helping improve the Pneumatic Sorting Digital Twin.

## Development setup

1. Install Unity `6000.6.0f1`, Git, and Git LFS.
2. Fork and clone the repository.
3. Run `git lfs pull` before opening Unity.
4. Open `Assets/Scenes/Kit1Viewer.unity`.
5. Run **Tools → Kit 1 Digital Twin → Validate Project**.
6. Complete the relevant checks in `Documentation/VALIDATION.md`.

## Change guidelines

- Keep PLC addresses outside mechanical simulation scripts.
- Add shared behavior to reusable services or HMI adapters rather than duplicating panels.
- Preserve safe behavior when control power, pressure, or communication is unavailable.
- Document calibrated strokes, axes, sensor assumptions, and test evidence.
- Do not commit generated Unity folders such as `Library`, `Temp`, `Logs`, or `UserSettings`.
- Use Git LFS for large CAD, mesh, texture, video, and recording assets.
- Only submit CAD and media that may legally be redistributed.

## Pull requests

Include:

- a concise description of the problem and solution;
- the Unity version used;
- validation steps and results;
- screenshots or recordings for visible changes;
- any new PLC, pressure, geometry, or safety assumptions.

Keep pull requests focused. Avoid reformatting unrelated assets or scripts.
