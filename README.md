# Immersive Inventory

Client-side mod for Vintage Story 1.23: the stack on the cursor hangs from the point you grabbed it by and swings like a pendulum. A torch grabbed by the handle flips over, a lantern hangs from its ring, a sharp flick of the mouse sets the item spinning. Not needed on the server.

Works great together with [Inventory Particles](https://mods.vintagestory.at/inventoryparticles): particles follow the rotated item (the flame of an upside-down torch still rises from its head), and weapons leave a trail when swung.

## How it works

- **Grab point** is where you clicked the slot. A click next to the model snaps smoothly to the nearest point of its silhouette.
- **Silhouette** is read by rendering the item once into a small offscreen buffer, so any item works without manual setup, modded and custom-rendered ones included. It gives the centre of mass, the moment of inertia and the tip used for swing detection.
- **Physics** is a rigid pendulum driven by gravity and mouse acceleration, with damping and air drag, stepped at a fixed 1/240 s regardless of FPS.

## Rules

Rules decide what swings: liquids, containers and plain blocks stay as in vanilla; weapons, lights, plants, food blocks and other items swing. Any mod can add rules as JSON, and the config's `DisabledRules` turns built-in ones off. See [docs/rules.md](docs/rules.md).

## Config

`ModConfig/immersiveinventory.json` (lengths in GUI pixels at interface scale 1):

| Field | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch; off draws the cursor as in vanilla |
| `Gravity` | `2000` | Pull on the centre of mass: more hangs and swings back faster |
| `Damping` | `1.8` | Share of angular speed lost per second |
| `AirDrag` | `0.015` | Extra braking of fast spin |
| `MaxSpin` | `35` | Angular speed limit, rad/s |
| `MouseInfluence` | `1` | How much mouse movement rocks the item; 0 is gravity only |
| `SnapTime` | `0.06` | Seconds for the grab point to catch up with the cursor after a snap |
| `SwingParticles` | `true` | Swing trail (needs Inventory Particles) |
| `SwingSpeed` | `420` | Tip speed that starts the trail, px/s, unless a rule sets its own |
| `DisabledRules` | `[]` | Rule codes to skip, e.g. `["weapons"]` |

A broken config is copied to `immersiveinventory.json.broken` and defaults are used.

## Commands

`.immersiveinventory <subcommand>`:

| Subcommand | Description |
|---|---|
| `reload` | Reload config and rules |
| `list` | List active rules in check order |
| `debug` | Toggle overlay (red: grab point, blue: centre of mass, green: tip) and rule logging |

## Inventory Particles

Optional. With it installed, the mod tells Inventory Particles where the cursor stack is drawn and how it is rotated, so all its cursor effects follow the item, and plays the `immersiveinventory:swing` effect along fast-swinging weapons. Requires an Inventory Particles version with `SetCursorRectProvider` and `GuiItemRect.Rotation`; otherwise a warning is logged and the mod runs without particles.

SmoothSwapping is not specially supported: its fly-in and fly-out animations may jump at the start and end.

## Build

Set `VINTAGE_STORY` to the game folder.

```bash
./build.sh                                     # Releases/immersiveinventory_<version>.zip
dotnet run --project ImmersiveInventory.Tests  # tests, no window or OpenGL
```

`ImmersiveInventory/libs/InventoryParticles.dll` is a compile-time reference only and is not packaged; after changing the Inventory Particles API, rebuild it and copy the fresh DLL there. Rider has `Client`, `Client + InventoryParticles` (uses the Debug build from `../InventoryParticles`) and `Server` launch profiles.

## License

Copyright (C) 2026 SecretLovers

This program is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. See [LICENSE](LICENSE).

SPDX-License-Identifier: GPL-3.0-or-later
