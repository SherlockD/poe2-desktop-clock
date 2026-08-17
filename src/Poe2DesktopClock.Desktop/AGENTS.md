# WPF presentation rules

`Desktop` contains views, view models, UI-specific adapters, and application startup. It must not contain Win32, OCR, HTTP, JSON persistence, or pricing logic.

- `App.xaml.cs` is the WPF composition root: obtain the base collection from `Poe2DesktopClockComposition`, register Desktop services/view models, build the provider, and dispose it on shutdown.
- `MainWindow` must receive its view model; never use it as a composition root.
- View models receive narrow `Application.Interfaces` ports through constructors. They do not construct services or access `DesktopClockRuntime`.
- Presentation text (including Russian status labels) belongs here. Do not add localized UI strings to Domain or Contracts.
- Keep WPF event handlers thin and use async shutdown carefully: cancel closing, await disposal, then close once.
