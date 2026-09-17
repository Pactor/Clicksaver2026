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

Rolling missions automatically (repeat the player's last Request until a watched item appears)
is the open item. History of the crash and where it stands:

1. The roll = replay of the client's internal `N3Msg_GenerateMissions` (carries the slider
   settings), **not** a click on the popup. `this` + the 40-byte `MissionGenerateInfo_t` are
   recorded when the player clicks Request; replaying calls the same function again.
2. That function is `__thiscall`, which Native AOT does **not** emit correctly. Fixed with
   hand-written x86 trampolines (`ClickSaver2026.Core/Hook/Trampolines.cs`, unit-tested). Do NOT
   use `delegate* unmanaged[Thiscall]` / `[UnmanagedCallersOnly(Thiscall)]` in AOT x86.
3. The account still crashed — but the user confirmed it crashes **only on an agent roll, never on
   a manual roll**. So the record + the replay call are fine; the crash was the **whole-window
   subclass** the agent used to run the replay on the game thread (SetWindowLongPtr on the
   "Anarchy client" main window + a managed WndProc on every message). That is now **removed**.
4. **First attempt (rejected by a live test):** replay from inside the `DataBlockToMessage` hook,
   guarded by `gameThread == GetCurrentThreadId()`. The retail client decodes server blocks on a
   **different** thread than the mission-terminal button, so the guard failed and the agent reported
   **"cannot roll from the hook"** — confirming DataBlock and the button run on different threads.
5. **Current mechanism (needs live verification):** the replay is pumped from a hook on the client's
   **message-pump** function. The retail engine (`Anarchy.exe`) imports `PeekMessageA` and calls it
   every frame on its UI thread — the same thread the Request button dispatches on. The hook
   (`PeekMessageDetour`, a correct `__stdcall` reverse callback) calls the real pump, then, if a roll
   is queued, replays `N3Msg_GenerateMissions` right there — a quiescent point between messages, on
   the button's own thread. No window subclass. `RunPendingRequest` keeps its
   `thread == GetCurrentThreadId()` guard as defense; on the pump thread it now matches. If the host
   imports no pump function at all, rolling reports `NotSupported` up front instead of hanging.
   Which pump is hooked: first of `PeekMessageA / PeekMessageW / GetMessageA / GetMessageW` found in
   the process's IATs (`MessagePumpImports` in `Hook.cs`).

### What to ask / check next
- After the user's next test, the two outcomes: (a) it rolls and matches — **done**; or (b) it still
  crashes or misbehaves on an agent roll — then the `PeekMessageA` replay point is not safe and we
  fall back to network-level replay (below).
- Fallback if on-thread replay still isn't safe: replay at the **network level** — capture the
  outgoing request packet when the player clicks Request and resend the bytes (never calls client
  C++), which cannot corrupt client state. `Connection.dll` is the module to hook for the send path.

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
