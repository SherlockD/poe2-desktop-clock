# Source-layer rules

Follow the repository-level `AGENTS.md`. Project references must point inward: Presentation → Application/Contracts/Domain; Infrastructure → Application/Contracts/Domain; Domain has no project references.

Use the solution folders as architectural documentation:

- `Infrastructure`: Windows, Poe API and storage adapters.
- `Presentation`: WPF Desktop and ConsoleDebug.
- `Application`, `Contracts`, `Domain`, and `Composition`: shared architecture projects.

`Composition` is the shared registration module. It may reference infrastructure and Application, but must never reference a presentation project; otherwise Desktop → Composition becomes a cycle.
