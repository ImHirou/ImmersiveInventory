# Physics rules

Rules decide which stacks swing on the cursor and how. Each file `assets/<domain>/config/immersiveinventory/rules/*.json` of any mod is an array of rules. They are checked from the highest `priority` to the lowest, and the **first** one that matches decides; a stack no rule matches is drawn as in vanilla. `.immersiveinventory reload` reads them again, `.immersiveinventory list` shows them in order, and the config's `DisabledRules` turns rules off by code.

```json
[
    {
        "code": "mymod-staffs",
        "priority": 700,
        "match": { "codes": [ "mymod:staff-*" ] },
        "gravity": 0.8,
        "swing": "mymod:magictrail",
        "swingSpeed": 300
    },
    {
        "code": "mymod-jars",
        "priority": 1000,
        "match": { "classes": [ "BlockMyJar" ] },
        "physics": false
    }
]
```

## Fields

| Field | Default | Description |
|---|---|---|
| `code` | — | Unique name, for `DisabledRules`, `list` and the debug output |
| `priority` | 0 | Higher is checked first |
| `match` | everything | What the rule applies to, see below |
| `physics` | `true` | `false` keeps the matched stacks as in vanilla |
| `gravity` | 1 | Multiplies the config's `Gravity`: more makes the item hang and swing back faster |
| `damping` | 1 | Multiplies the config's `Damping` |
| `inertia` | 1 | Multiplies the moment of inertia around the centre of mass: more turns slower |
| `stiffness` | 0 | How firmly the item keeps the angle it had in the slot (a spring, per second squared). 0 is a free pendulum; a few thousand keep it nearly upright |
| `swing` | — | Inventory Particles effect played along the item while it swings fast. Without a domain, the rule file's domain is used: `swing` in this mod's file is `immersiveinventory:swing` |
| `swingSpeed` | config | Speed of the tip around the grab point from which the swing effect plays, GUI pixels per second |
| `grip` | — | Places the item can be held by, see below. Without them the grab point snaps to the item's silhouette |

## Match

All given conditions must hold; within a list one entry is enough.

| Field | Description |
|---|---|
| `codes` | Item and block codes with `*` (via `WildcardUtil`); a leading `@` makes a regular expression. Without a domain, `game` is assumed |
| `classes` | Registered class names (`BlockTorch`, `ItemLiquidPortion`…); subclasses match too. Unknown names are logged, and a rule whose classes are all unknown never matches |
| `tools` | Tool types from `EnumTool`, case doesn't matter: `sword`, `spear`, `axe`, `knife`, `pickaxe`… |
| `type` | `item` or `block` |
| `food` | `true` for collectibles with nutrition (`NutritionProps`), `false` for the others |
| `drawTypes` | Draw types of blocks (`Cross`, `JSON`, `Cube`…); a trailing `*` matches the beginning: `Cross*` |

## Grip zones

By default the item hangs from wherever it was clicked, and a click next to the item moves to the nearest point of its silhouette. `grip` limits that to some places: the grab point moves to the nearest point of the nearest zone. The units are those of Inventory Particles: 1.0 is the edge of a slot, around the centre of the item as drawn in a slot, +Y up.

```json
"grip": [
    { "line": [ -0.1, -0.3, -0.05, 0.05 ] },
    { "rect": [ 0, 0.3, 0.2, 0.1 ] },
    { "point": [ 0.25, 0.25 ] }
]
```

| Zone | Values |
|---|---|
| `point` | `[x, y]` |
| `line` | `[x1, y1, x2, y2]` |
| `rect` | `[centreX, centreY, width, height]` |

## Built-in rules

| Code | Priority | Matches | Physics |
|---|---|---|---|
| `liquids` | 1000 | `ItemLiquidPortion` | no |
| `containers` | 1000 | barrels, buckets, pots, crates, crocks, display cases, chests and trunks, molds, jugs and bowls, loot vessels, meals, mold racks, plant containers, skeps, crucibles, watering cans | no |
| `weapons` | 600 | tools `sword`, `spear`, `javelin`, `pike`, `halberd`, `poleaxe`, `polearm`, `club`, `mace`, `warhammer`, `staff`, `axe`, `knife`, `scythe`, `sickle` | yes, with `swing` |
| `lights` | 500 | `BlockTorch`, `BlockLantern`, `BlockOilLamp`, `BlockChandelier`, `BlockCandle`, `BlockBunchOCandles` | yes |
| `paperlanterns` | 500 | `game:paperlantern-*` | yes |
| `food-blocks` | 400 | edible blocks | yes |
| `plants` | 300 | plants, mushrooms, saplings, reeds, ferns, berry bushes, water plants, vines… | yes |
| `cross-blocks` | 290 | blocks drawn as a cross | yes |
| `blocks` | 10 | all other blocks | no |
| `items` | 0 | all other items | yes |

## The swing effect

`swing` names an Inventory Particles effect (see its `docs/api.md`). It plays at three points along the line from the grab point to the tip, at 60, 80 and 100 % of the way, as long as the swing lasts; its intensity follows the speed of the tip (0.3 to 2.5 times the effect's rate). Screen space particles with a short life make a trail. The bundled `immersiveinventory:swing` is in `assets/immersiveinventory/config/inventoryparticles/effects/swing.json`.
