# Test rules

Tests mirror architectural boundaries. Prefer fast, deterministic unit tests for Domain, Contracts, calculation and persistence behaviour. Use infrastructure integration tests only for real adapter contracts that cannot be represented as units.

Do not require a running PoE client, desktop interaction, a real account, or external network access in the normal test suite. For API/Windows smoke checks, add explicit opt-in diagnostics rather than making `dotnet test` flaky.

Cover data migration from `%LOCALAPPDATA%\Poe2DeskTracker`, valuation completeness, status transitions, cancellation/shutdown, and concurrent scan behaviour as these areas change.
