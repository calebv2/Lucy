# Lucy

Lucy adds the complete sword to **A Township Tale** as a weekly mine find. One new sword is allocated per server each Monday at 6:00 a.m. America/New_York time. The server selects an internal mine layer from 0 through 100 and waits for that layer to load naturally before choosing a safe floor position. Previously found swords remain in the world.

## Install v0.2.4

Install the same `Lucy.dll` from the release ZIP in the `Mods` folder of the dedicated server and every client that joins it. The mod requires MelonLoader and SyncLib. Restart both sides after replacing the DLL. Keep the world's existing `modHashIds.json` and `LucyWeekly/ledger.json`; they preserve custom item and weekly allocation state.

If an older test build called `LucyMould.dll` is installed, remove that DLL so only `Lucy.dll` loads. The old registry name is not interchangeable with the current Lucy name for existing saves.

The server creates `UserData/LucyWeekly/settings.json` with the fields shown in [`Settings.example.json`](Settings.example.json). `enabled` can pause new placement. `minLayer` and `maxLayer` affect the next unplanned week. Valid settings changes are read while the server is running.

Check the startup log for `LUCY_START version=0.2.4`, `LUCY_WEAPON_REGISTERED`, and `LUCY_WEEK_READY`. `LUCY_WEEK_PLANNED` reports the selected layer; `LUCY_WEEK_SPAWNED` reports placement. A server that is offline for a week does not create a missed sword on return.

## Manual spawn (admin)

After Lucy has registered on the server and client, enter this in the server console or Dashboard command runner:

```text
spawn "<ATT username>" LucySword
```

Replace `<ATT username>` with the online player's name. The registered prefab is `LucySword`; `LucyBlade` and `LucyMould` are inactive legacy identifiers. A manually spawned sword does not use the week's mine allocation.

## Build

The repository contains the mod source and its required graphics bundle. It does not include A Township Tale, Unity, MelonLoader, or SyncLib binaries. Build on Windows with the installed game, SyncLib in its `Mods` folder, and Unity 2020.3.23f1:

```powershell
.\build.ps1 -GamePath 'C:\path\to\A Township Tale' -UnityEditorPath 'C:\path\to\Unity\2020.3.23f1\Editor'
```

The output is `bin/Lucy.dll`. The `assets/lucy-graphics` bundle is embedded in the DLL for client rendering; a headless server does not load that art. The code registers the sword from current native pickup and combat components, so both sides must use a compatible game build.

## Scope

This release only places the complete sword in the mines. It does not add Lucy moulds to loot or restore mould casting. The inactive legacy identifiers remain in code for save continuity. Found swords are ordinary pickups and are not removed by the weekly reset.
