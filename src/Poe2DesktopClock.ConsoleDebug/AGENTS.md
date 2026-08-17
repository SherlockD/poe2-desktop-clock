# Diagnostic console rules

`ConsoleDebug` is a presentation/diagnostic adapter. It may parse commands, print progress and call Application use cases or explicit diagnostic adapters. It must not duplicate valuation, scanning, persistence, or API workflow from production runtime code.

Keep `Program.cs` as composition and command dispatch only. Put command families in focused services:

- game-window/capture diagnostics;
- Currency setup, calibration and scan diagnostics;
- public-stash setup and scan diagnostics;
- combined valuation display.

Use constructor injection for the services rather than global state. When a diagnostic scenario becomes useful to WPF too, promote it to an Application use case instead of copying its logic.
