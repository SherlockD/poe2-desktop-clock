# Windows infrastructure rules

This project owns Windows-only adapters: process/window discovery, capture, overlay/calibration UI, OCR and monitoring orchestration. It may implement Application use cases but must not depend on WPF Desktop.

`DesktopClockRuntime` is the application façade for the existing tracking scenarios. Keep it an orchestrator: move focused persistence, setup, calculation, snapshot composition, and scheduling work into dedicated classes. Its injected dependencies are owned by DI; do not dispose them in the runtime. Its parameterless constructor is legacy-only and owns the dependencies it creates.

HTTP clients belong in `Infrastructure.PoeApi`; JSON stores belong in `Infrastructure.Storage`. Do not add such code here merely because the desktop application currently consumes it.

Background monitoring must expose a tracked task and honour cancellation before disposal. Serialize shared scan state and publish coherent snapshots.
