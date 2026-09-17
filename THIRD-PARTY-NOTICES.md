# Third-party notices

## ClickSaver

ClickSaver2026 is a rewrite of ClickSaver, the Anarchy Online mission helper, and follows its
design: a DLL loaded into the client that hooks `DataBlockToMessage` in `MessageProtocol.dll`.
ClickSaver was released under the GNU General Public License version 2 or later.

- Copyright (C) 2001, 2002 MORB
- Copyright (C) 2003, 2004 Gnarf
- Updates by Darkbane, Uragon, Adjuster and others
- Source used as reference: https://github.com/pzychotic/ClickSaver (v2.5.3)

The reward-item, icon and playfield reading in `ClickSaver2026.Core/GameData` is adapted from
OmniCell's AssetDecoder (GPL-3.0). No other third-party code is vendored: the hook is C# compiled
with .NET Native AOT and hooks the client through its import tables, so it needs no detour library.
