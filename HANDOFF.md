# ClickSaver2026 — handoff

A rewrite of ClickSaver, the Anarchy Online mission helper, at `E:\Funcom\ClickSaver2026`
(local git only, no remote, do not push). All C#. The user (Pactor) tests on the live game;
commits go out under their name with no Claude attribution.

## What works

- **Attach + display.** The hook (C# Native AOT, 32-bit DLL) is loaded into each client and
  forwards decoded messages to the app over a named pipe. The Missions tab lists each attached
  account and shows that account's **current roll only** (fire-and-forget, no history), with each
  reward item's name, icon, QL and value (read straight from the client's ResourceDatabase), plus
  location, credits, XP and the find-item. Manual rolling with the hook attached is stable.
- **Watch + alert.** Multi-line watch list (one item name per line, substring so partial/exact
  both match, `-exclude` per line). On a match the buying agent stops and the app pops up + plays a
  sound + comes to front. **Never auto-accepts** — the mission popup is the client's own custom UI,
  not Win32 controls, so we never click it.
- **Lifecycle.** Auto-attach to every client; Detach all / Re-attach all buttons on the Hook tab;
  the app detaches cleanly on close (so it never leaves a hooked, subclassed client behind).
- **Tests.** 60 pass (`build.ps1 -Test`). Includes trampoline byte tests, the pipe/command servers,
  the mission parser vs. retail captures, and loading the real hook into a stand-in client.

## THE CURRENT PROBLEM — auto-roll (buying agent)

Rolling missions automatically (repeat the player's last Request until a watched item appears) is
the open item. The mechanism was wrong for a long time; here is the corrected model and where it
stands.

### What rolling actually is (ground truth, from ClickSaver 2.5.3's ReadMe)
The **original** ClickSaver buying agent did **not** call any internal function. It **simulated
mouse clicks** at fixed screen pixels: snap the mission box to the screen's top-left, alt-tab so the
game has mouse focus, then it "clicks twice on the leftmost pixel of the difficulty slider, twice on
the rightmost, then clicks Request." That is the clunky mouse-hijack the user wanted to improve on.
The old DLL hook was **only for reading** mission info to display — never for rolling.

So the earlier rewrite premise was wrong: replaying the internal `N3Msg_GenerateMissions` does
nothing useful (its name/role is the incoming mission path, not the outgoing request). Two dead
attempts, both removed: (a) replay from the `DataBlockToMessage` hook — refused, wrong thread;
(b) replay pumped from a `PeekMessageA` hook on the UI thread — "says rolling but does nothing."

### Current mechanism — network-level replay (needs live verification)
Roll by **resending the exact outgoing request bytes** the client already sends when the player
clicks Request. No mouse, no UI thread, no client-UI reentrancy. Verified against the retail
binaries with `dumpbin`:
- `Interfaces.dll` imports `Connection_t::Send(unsigned int id, const Message_t&)`
  (`?Send@Connection_t@@QAEHIABVMessage_t@@@Z`) — the send the mission request goes through.
- `Connection.dll` **exports** the raw `Connection_t::Send(id, size, const void* data)`
  (`?Send@Connection_t@@QAEHIIPBX@Z`), and sending holds a lock (`m_cSendLock@TcpHandler_t`), so it
  is **thread-safe** — replay can run from the command-pipe thread.
- `MessageProtocol.dll` exports `DataBlockSizeGet@Message_t` and the `CreateDataBlock` overrides,
  used to serialise a live message to its wire bytes.

Flow in `Hook.cs`:
1. `OnGenerate` (Request-button hook) records the 40-byte slider info for display and **arms**
   capture: `expectSend = true`, `expectSendThread = current`.
2. `OnSend` (IAT hook on `Connection_t::Send(Message_t&)` in Interfaces.dll, via the new
   `ThiscallToCdeclDetour2` 2-arg trampoline) fires on the next send on that thread, snapshots the
   message bytes (`SnapshotMessage` → finds the object's `CreateDataBlock` in its vtable, calls it +
   `DataBlockSizeGet`), and stores `(Connection*, id, bytes)`. The real send still happens.
3. A roll command calls `DoRoll` **on the command thread**: resends the bytes through the exported
   raw `Send` (`CdeclToThiscallCall3` trampoline). Returns `Done`.
Capability `CanRequestMissions` is now gated on the whole chain (`RollReady`): Request hook + Send
hook + raw send + serialisers all resolved. New trampolines are unit-tested (`TrampolineTests`).

### What to check next (the app now shows a diagnostic)
The buying-agent status reads `Rolling N of M... (replayed X, new lists Y, last Z)`:
- **last = NothingRecorded** → capture missed: the request send did not come through
  `Connection_t::Send` on the armed thread (maybe queued/async). Adapt capture (e.g. capture at the
  raw exported `Send` via an inline detour, or widen the arm window).
- **last = Done, replayed climbs, new lists = 0** → replay sends but the server does not roll: the
  `id` passed to the raw `Send` may differ from what the `Message_t&` overload forwards, or the
  bytes need the overload's framing. Inline-detour the raw `Send` to capture exactly what it sends.
- **last = Done, replayed climbs, new lists climb** → it works.
- **last = NotSupported** → `RollReady` is false: one of the imports/exports was not found (check the
  module names / that Connection.dll + MessageProtocol.dll were loaded when hooks installed).

## Build / run

- Close all AO clients and the app first: a running client keeps the hook DLL **file-locked**, so
  the build can't overwrite `Build\Release\ClickSaver2026.Hook.dll`.
- `./build.ps1 -Clean -Test` (needs .NET 10 SDK + VS2022 C++ tools for the AOT linker). Runnable app:
  `Build\Release\ClickSaver2026.exe` with the hook DLL beside it.
- Test flow: attach → at a terminal, click Request once by hand on the account to roll → select that
  account on the Missions tab → type watch item(s) → Start.

## Layout (all projects at repo root; output in `Build\`)

- `ClickSaver2026.App` — WPF, 32-bit. `ClickSaver2026.Core` — parser, ResourceDatabase, watch,
  pipe/command servers, `BuyingAgent`, `Trampolines`. `ClickSaver2026.Hook` — Native AOT hook DLL
  (IAT hooking; `Hook.cs`, `IatHook.cs`, `Trampolines.cs` linked in). `ClickSaver2026.HookHarness`
  — Native AOT stand-in client for tests. `ClickSaver2026.Tests` — xUnit v3, 32-bit.

Retail mission captures for the parser: `E:\Funcom\captures\mp_200346_s2.csv` (server lines: 16
plain bytes then one zlib stream). Set `CLICKSAVER2026_RETAIL_CSV` and `AO_CLIENT` to run the
optional retail tests.
