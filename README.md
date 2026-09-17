# ClickSaver2026

A rewrite of ClickSaver, the Anarchy Online mission helper: it shows every mission a terminal
offers - above all the reward items - in one window, alerts on the items, places and mission
types you watch for, and rolls missions for you.

**Status: early.** Rolled missions show up in the Missions tab with their reward items (name, icon,
QL, value), type, location, credits, XP and the item to find. The parser is checked against real
retail terminal answers; watch lists, alerts and the buying agent are still to come.

## Layout

| Part | What it is |
|---|---|
| `src/ClickSaver2026.Hook` | C++20, 32-bit DLL. Loaded into the client; detours `DataBlockToMessage` in `MessageProtocol.dll` and forwards every decoded message over the pipe `\\.\pipe\ClickSaver2026`. |
| `src/ClickSaver2026.Core` | C# .NET 10. Pipe server, attaching to clients, capture files, the mission list parser, and item names, icons and playfields read straight from the client's ResourceDatabase. |
| `src/ClickSaver2026.App` | C# .NET 10 WPF, 32-bit. The window. |
| `tests/ClickSaver2026.Tests` | xUnit v3, 32-bit. Includes an end-to-end test that attaches the real hook to the harness. |
| `tests/HookHarness` | A stand-in client: a `MessageProtocol.dll` exporting the same `DataBlockToMessage`, and `HookHost.exe` calling it. |

## Building

Needs Visual Studio 2022 (or Build Tools) with the C++ workload, CMake 3.25+ and the .NET 10 SDK.

```powershell
./build.ps1          # Release
./build.ps1 -Test    # and run the tests
```

The runnable app ends up in `out\ClickSaver2026.exe`, with the hook DLL next to it. It is a 32-bit app, so it needs the x86 .NET 10 Desktop Runtime.

## Using it

Start the app with the client running (either order works). With *Attach automatically* on it
loads the hook into every client it finds; *Detach* unloads it again. If the client runs as
administrator, the app must too.

The Missions tab needs the Anarchy Online folder for item names and icons: it is found from the
running client (*Detect*) or picked with *Browse*, and remembered. Nothing is copied or built - the
client's own database is read when an item is shown.

The Hook tab lists every message the client decodes. Tick *Save messages to a capture file* to
record them to `%LOCALAPPDATA%\ClickSaver2026\Captures`; those recordings are what the mission
parser is being built from.

## Tests

`./build.ps1 -Test`. Two optional tests use data that is not in the repository: set
`CLICKSAVER2026_RETAIL_CSV` to a retail stream CSV with mission rolls and `AO_CLIENT` to an
Anarchy Online folder; without them those tests are skipped.

## Licence

GPL-3.0-or-later. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
