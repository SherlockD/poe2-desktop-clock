# PoE 2 Desk Tracker

The console app supports two independent sources of stash quantities: an on-screen dedicated currency-tab scanner and public named stash tabs read through the PoE 2 Trade API. It waits for a visible Path of Exile 2 window only for commands that capture the game window.

## Run

```powershell
.\run.ps1
```

With PoE 2 running and visible, enter `debug-frame`. The only intentional disk write is that explicit diagnostic image.

`run.ps1 -BuildOnly` performs a build without starting the tracker. The script uses `C:\Temp` for its local NuGet cache and intermediate C#/WinRT files: this avoids a current C#/WinRT limitation with Cyrillic project or package paths.

## Currency region

Set the one capture region used for the currency tab:

```text
currency setup
```

Drag over the area in the overlay, then press `Enter` to save it (`Esc` cancels and right-click resets the selection). The tracker restores a minimized game window, activates it, and waits for its client area to become stable before opening the overlay.

Each calibration or scan saves a fresh cropped preview to `debug/regions/currency.png`. Region settings are kept in `%LOCALAPPDATA%\Poe2DeskTracker\regions.json`, independent of whether the app is launched from Visual Studio or `run.ps1`.

To detect and review currency-slot frames in a saved region:

```text
currency calibrate
```

The calibration window places green frames over the detected currency slots. It finds their gold border lines and recurring frame geometry from the image itself, so changing the selected region's size, surrounding padding, or monitor resolution does not change the layout logic. Frames belonging to the same row are aligned to a shared vertical level. The lower two-row storage grid and the auxiliary four-slot strip above it are excluded automatically. Currency names come from the fixed 33-slot tab profile, so empty slots remain identified. Drag a frame with the left mouse button to correct its position. Press `Enter` to save the layout, or `Esc` to cancel. Layouts are saved to `%LOCALAPPDATA%\Poe2DeskTracker\currency-layouts.json`.

To read all quantities from that saved layout:

```text
currency scan
```

The scanner captures a fresh image, reads only the bright stack-count text in the upper-left of each slot, and parses any contiguous sequence of digits into a 64-bit integer. Its annotated diagnostic image is written beside the region preview as `currency-amounts.png`.

To discard the region and calibration and begin again:

```text
currency reset
```

## Public stash tabs

This mode reads public, trade-visible premium tabs by a unique deliberately-high
listing price configured for each tab. It is intended for tabs such as Omens,
Runes, Essences, or other special-item storage. It does not inspect the game
screen, send input, or require `POESESSID`.

Run the guided setup:

```text
public setup
```

Enter the account name and league. The setup first prints the ready-to-use
in-game names for all eight categories, such as `~price 1001 mirror` for Breach,
`~price 1002 mirror` for Abyss, and so on. Set those names on the corresponding
public tabs, then press `Enter` once to save the complete configuration. The
price itself is the marker; do not append extra text to it.

Every item in one tracked tab must inherit that tab's default price. Set the
price once on the tab itself; do not override individual item prices, because a
different price cannot be located by that tab's marker query.
Use a real Trade currency such as `mirror`, `divine`, or `exalted`; `waystone` is not a
valid Trade price currency.
The marker prevents accidental sales and gives the Trade API a precise way to
retrieve every listing from that one tab, because Trade has no filter for stash
tab name.

The selected names, marker prices, and account settings are saved in
`%LOCALAPPDATA%\Poe2DeskTracker\public-stash.json`. Existing name-only settings
are preserved for reference but must be reconfigured once with `public setup`.

Then read every selected tab with one exact marker-price query followed by batched
Trade API fetches:

```text
public scan
```

The result is grouped first by configured stash-tab name and then by item name;
stack sizes are summed, so separate stacks of the same Omen or Rune produce one
quantity. It has no filter for `currency`, item category, or item name: all item
types placed in the marked tab are eligible.
After that scan, the tracker loads the current poe.ninja economy snapshot and
prints the unit price, tab total, and overall total in Divine Orbs. Items without
a current price are shown with `?` and excluded from the estimate.

To get one combined inventory estimate, first complete both the currency
calibration and public-tab setup, open the dedicated currency tab in-game, then
run:

```text
worth scan
```

It takes one current price snapshot for the configured league, reads the 33
dedicated currency slots from the screen, scans the selected public tabs, and
prints their combined value in Divine Orbs. The tracker maps the Russian
currency-tab labels to the English price-feed names internally. An unreadable
screen count or an item without a price is shown as `?` and is excluded from the
combined total rather than treated as zero.

Useful configuration commands:

```text
public list
public add "Custom category"
public remove 2
public remove "Exact tab name"
public reset
```

An empty tab cannot be proven empty by Trade API alone. A tab must be public
in-game before its contents can appear in a scan, and Trade indexing is not
instantaneous after moving items. The default setup labels are Разлом, Бездна,
Ритуал, Экспедиция, Делириум, Сущности, Руны, and Фрагменты. Dedicated core
currency remains in the screen-scanned currency-tab mode.

The Trade API supplies at most 100 listings per marker-price query. If a marker
matches more than 100 listings, a configured tab is absent from the result, or a
marker also matches another tab, the tracker marks the scan incomplete and
refuses to publish a combined `worth scan` total. A full eight-tab scan takes
around 80 seconds because requests are spaced to respect the Trade API's rolling
rate limit.

The project uses the installed Windows SDK 10.0.26100 for compilation and runs on x64 Windows 10 version 1903 or newer. Windows Graphics Capture itself requires Windows 10 version 1903 or newer. It does not inspect game memory or send input to the game.
