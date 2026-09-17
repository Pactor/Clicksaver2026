# ClickSaver2026

A rewrite of ClickSaver, the Anarchy Online mission helper: it shows every mission a terminal
offers - above all the reward items - in one window, alerts on the reward items you watch for, and
rolls missions for you without taking over the mouse.

**Status: working, but lightly tested.** Rolled missions show up in the Missions tab with their
reward items (name, icon, QL, value), type, location, credits, XP and the item to find. The buying
agent repeats your last hand-roll (same difficulty/sliders) over and over until a reward item you
watch for appears, then stops and alerts you to accept it. It rolls at the network level - it
resends the request the client already sent - so it never touches the mouse or the game's UI.

The mission parser is checked against real retail terminal answers, and the buying agent has been
confirmed on the live client. **More live testing is needed**: it has only been exercised on one
account/setup, so team rolls, other terminal types, and edge cases still want real-world hours. Test
and please report anything that misbehaves.

## Layout

Every project sits at the repository root; everything a build produces goes into `Build\`. It is all
C#: the hook and the test harness are compiled with .NET Native AOT to native 32-bit binaries.

| Project | What it is |
|---|---|
| `ClickSaver2026.App` | C# .NET 10 WPF, 32-bit. The window. |
| `ClickSaver2026.Core` | C# .NET 10. Pipe server, attaching to clients, capture files, the mission list parser, and item names, icons and playfields read straight from the client's ResourceDatabase. |
| `ClickSaver2026.Hook` | C# .NET 10, Native AOT, 32-bit native DLL. Loaded into the client; hooks `DataBlockToMessage` (and the Request-missions call) through the import tables and forwards every decoded message over the pipe `\.\pipe\ClickSaver2026`. |
| `ClickSaver2026.Tests` | xUnit v3, 32-bit. Includes end-to-end tests that load the real hook into the harness. |
| `ClickSaver2026.HookHarness` | C# Native AOT. A stand-in client: a `MessageProtocol.dll` exporting the same `DataBlockToMessage`, and `HookHost.exe` calling it. |

| `Build\` | |
|---|---|
| `Release\`, `Debug\` | The runnable app: `ClickSaver2026.exe` with the hook DLL beside it |
| `Harness\` | The stand-in client |
| `Tests\` | Test assemblies |
| `obj\`, `bin\`, `TestResults\` | Intermediate output |

## Building

Needs the .NET 10 SDK and the Visual Studio 2022 C++ build tools (Native AOT links the hook
and harness with the Microsoft linker).

```powershell
./build.ps1          # Release, into Build\Release
./build.ps1 -Test    # and run the tests
./build.ps1 -Clean   # delete Build\ first
```

It is a 32-bit app, so it needs the x86 .NET 10 Desktop Runtime.

## Using it

Start the app with the client running (either order works). With *Attach automatically* on it
loads the hook into every client it finds; *Detach* unloads it again. If the client runs as
administrator, the app must too.

The Missions tab needs the Anarchy Online folder for item names and icons: it is found from the
running client automatically, or set with *Detect* / *Set folder* (those buttons hide once it is
found), and remembered. Nothing is copied or built - the client's own database is read when an item
is shown.

**Buying agent.** Roll a mission at a terminal once by hand (this records the exact request,
difficulty and sliders included). Select that account on the Missions tab, type the item name(s) to
watch for - one per line, matched as a substring so partial or exact both work, with `-exclude` and
`"quoted phrase"` allowed - and press *Start*. The agent repeats your roll until a reward item
matches, then stops, comes to the front, plays a sound and names the item so you can accept it. It
never auto-accepts. To change the difficulty, adjust the slider and hand-roll again - the agent
picks up whatever your last hand-roll set. Note that mission QL is random within the slider's range,
so high-QL rewards are rare; the agent just rolls until one you want turns up.

The Hook tab lists every message the client decodes. Tick *Save messages to a capture file* to
record them to `%LOCALAPPDATA%\ClickSaver2026\Captures`; those recordings are what the mission
parser is being built from.

## Possible additions

The buying agent currently matches on **reward item name** only. Natural next steps, several of
which the original ClickSaver had:

- **Watch by area / location** - match a mission's playfield (and coordinates), e.g. only alert on
  missions in Borealis.
- **Watch by mission type** - match the mission kind, e.g. only *find person* (or *kill person*,
  etc.).
- **Combine filters** - require all of them together, e.g. *reward "anger" **and** in Borealis
  **and** find-person only*.
- **Watch by reward QL** - roll until any reward item is at least a chosen QL.
- **Highlight** matching missions on the cards, and sort a roll by best reward QL.

## Tests

`./build.ps1 -Test`. Two optional tests use data that is not in the repository: set
`CLICKSAVER2026_RETAIL_CSV` to a retail stream CSV with mission rolls and `AO_CLIENT` to an
Anarchy Online folder; without them those tests are skipped.

## Licence

GPL-3.0-or-later. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
