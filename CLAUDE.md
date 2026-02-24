# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Branding & Legal

- **Legal entity:** Vital Blocs LLC (used in copyright, file properties, installer publisher)
- **Product brand:** DriveFlip (user-facing name — standalone brand, not "Vital Blocs DriveFlip")
- **Website:** https://driveflipapp.com (product site, downloads, privacy policy, terms)
- **Internal:** DriveFlip is part of the VitalBlocs product portfolio, but externally it presents as its own brand

## Build & Run

```bash
dotnet build
dotnet run
```

The app requires Administrator elevation (enforced via `app.manifest`). No test projects exist.

## Tech Stack

- .NET 9 WPF (`net9.0-windows`), C# with nullable enabled
- **WPF-UI** (Fluent Design System) — `FluentWindow`, `TitleBar`, `Card`, `ProgressRing`, dark theme
- **System.Management** — WMI queries for drive detection and SMART health data
- P/Invoke for raw disk I/O (`CreateFile`, `DeviceIoControl`, `SetFilePointerEx`)

## Architecture

MVVM with services layer. No DI container — ViewModels instantiate services directly.

**Data flow:** `DriveDetectionService` enumerates physical drives via WMI → `MainViewModel` manages drive collection, selection, commands → `DiskEngine` performs raw disk I/O for surface checks and wipes → `ReportService` generates text reports.

**Key files:**
- `ViewModels/MainViewModel.cs` — Central orchestrator: all commands, settings state, async operation management. Uses `SafeAsync()` wrapper for async void exception safety.
- `Services/DiskEngine.cs` — Low-level disk operations via P/Invoke. Surface check (random sampling), Smart Wipe (head+tail+scatter), Full Wipe (sequential). 256-sector (128 KB) buffer size.
- `Services/DriveDetectionService.cs` — WMI queries (`Win32_DiskDrive`, `MSFT_PhysicalDisk`, `MSFT_StorageReliabilityCounter`) with 15-second timeouts.
- `Models/PhysicalDrive.cs` — All data models, enums (`WipeMethod`, `WipeMode`, `OperationType`), health info tiers, risk assessment, report classes.
- `Views/MainWindow.xaml` — Full UI: two-column layout (drives list + actions panel), settings popup, Info/Health tabs.
- `Views/StyledDialog.xaml` — Custom modal dialog replacing all MessageBox usage. Four semantic types: `ShowInfo`, `ShowQuestion`, `ShowWarning`, `ShowDanger`.

## Conventions

- **Dialogs:** Always use `StyledDialog` static methods, never `MessageBox`.
- **Theming:** Dark theme via `<ThemeMode>Dark</ThemeMode>` in csproj. Semantic accent brushes defined in `App.xaml`: `AccentBlue`, `AccentGreen`, `AccentRed`, `AccentAmber`.
- **Converters:** Custom value converters live in `Converters/Converters.cs` and are registered as app-level resources in `App.xaml`.
- **Async commands:** Wrap async lambdas in `SafeAsync()` to catch and display unhandled exceptions.
- **Multi-drive operations:** Run in parallel via `Task.WhenAll`; UI updates use `Dispatcher.Invoke`.
- **SMART data refresh:** `DriveHealthInfo` doesn't implement `INotifyPropertyChanged` — force a DataContext re-bind (`SelectedDrive = null` then reassign) to update the Health tab.
- **System drive protection:** The C: drive is automatically excluded from selection/wipe operations.
- **Destructive operations:** Require two-step confirmation (Warning dialog → Danger dialog) before execution.
- **Logging:** Thread-safe file logger writes to `%LocalAppData%\DriveFlip\DriveFlip_Log_YYYYMMDD.txt`.
