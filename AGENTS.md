# PoE 2 Desktop Clock — guide for contributors

## Purpose

Windows desktop tracker for the estimated value of a PoE 2 Currency tab and configured public stash tabs. It captures only the game window; it does not read game memory or send input to the game.

## Solution map

```text
Presentation (Desktop WPF, ConsoleDebug)
             ↓ uses
Application (use-case interfaces)
             ↓ contracts
Domain (product rules, value objects)
             ↑ implemented by
Infrastructure (PoeApi, Storage, Windows)
             ↑ wired by
Composition + Desktop/App.xaml.cs
```

- `Domain` is pure business logic: no UI, Win32, OCR, HTTP, files, or Application dependency.
- `Contracts` contains boundary DTOs. Keep them transport-oriented and presentation-neutral.
- `Application` defines focused use-case interfaces; it must not depend on infrastructure or presentation.
- `Infrastructure.*` provides adapters. `Windows` is the only layer allowed to use Win32/WGC/WinForms/OCR; `PoeApi` and `Storage` target plain `net8.0`.
- `Desktop` is WPF presentation. View models depend on Application interfaces, never concrete infrastructure implementations.
- `ConsoleDebug` is a diagnostic presentation adapter, not application business logic.

## Dependency injection and lifetime

`Poe2DesktopClockComposition.CreateServiceCollection()` registers shared infrastructure and application ports. `Desktop/App.xaml.cs` adds WPF-only services and view models, builds the provider, and owns its disposal. Do not construct infrastructure clients or `DesktopClockRuntime` inside views/view models.

Singletons are deliberate for the runtime, API/capture clients, and single-window view models. They own long-lived state or resources. Do not put per-operation DTOs, scans, or mutable operation state in singletons. If multi-window UI or independent concurrent operations are added, create explicit scopes.

`DesktopClockRuntime` serializes scan work and owns monitoring state. Its default constructor is for compatibility; production composition must use its injected constructor.

## Change rules

1. Preserve existing data under `%LOCALAPPDATA%\Poe2DeskTracker`; new desktop settings reside in `%LOCALAPPDATA%\Poe2DesktopClock`.
2. Add a focused Application port before exposing a new scenario to WPF or ConsoleDebug. Do not grow a catch-all runtime interface.
3. Keep cancellation, shutdown, and disposal explicit for background work. Never start untracked fire-and-forget work that can outlive the application.
4. Add or update tests with changes to rules, persistence formats, parsing, valuation, or concurrency-sensitive behaviour.
5. Do not reintroduce `InternalsVisibleTo` for a presentation project; expose a use case or diagnostic adapter instead.

## Validation

```powershell
dotnet build Poe2DesktopClock.sln -c Debug -p:Platform=x64 --no-restore
dotnet test Poe2DesktopClock.sln -c Debug -p:Platform=x64 --no-restore
```

The solution targets Windows x64 for capture-related projects. Run the WPF or diagnostic console project directly with `dotnet run --project <project-path> -p:Platform=x64`.
