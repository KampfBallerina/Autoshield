# Autoshield

A client-side BepInEx mod for Valheim. Mark one shield and it is equipped automatically whenever you draw a compatible one-handed weapon, and put away again when you switch to something that can't be used with a shield.

## Features

- **Mark a shield** by hovering over it in your inventory and pressing the mark key (middle mouse button by default). Press again to unmark. Only one shield is marked at a time.
- **Auto-equip:** drawing a one-handed weapon equips the marked shield.
- **Auto-stow:** two-handed weapons, bows, torches, hammers and other tools put the shield away. It stays marked and returns with the next one-handed weapon.
- **Follows your choice:** if a shield is marked and you equip a different shield by hand, the mark moves to that one. Nothing is marked automatically if you had no mark before.
- **Visual marker:** two slim gold sparks circle the marked shield in the inventory and on the hotbar (including the quick-slot bar of *Equipment and Quick Slots*), plus an optional `[Auto Shield]` line in the item description.
- The mark is saved on the item itself, survives restarts and is removed when the shield is dropped.
- Client-side only: no server install needed and no version check between players.

## Configuration

Created on first start in `BepInEx/config/KampfBallerina.autoshield.cfg`:

| Option | Default | Description |
|---|---|---|
| `MarkKey` | `Mouse2` | Key or mouse button used to mark a shield while hovering it. Any Unity `KeyCode` (e.g. `Mouse3`, `F`, `LeftAlt`). `None` falls back to the middle mouse button. |
| `StowShieldWithWeapon` | `true` | Also put the marked shield away when the one-handed weapon is put away. |
| `ShowTooltipText` | `true` | Add the `[Auto Shield]` line to the item description. If `false`, only the border marks the shield. |

## Installation

1. Install [BepInEx for Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/).
2. Copy `Autoshield.dll` to `BepInEx/plugins/Autoshield/`.

## Building

Requires the .NET SDK and a Valheim installation with BepInEx.

```
dotnet build -c Release -p:ValheimDir="C:\path\to\Valheim"
```

The build copies `Autoshield.dll` to `BepInEx/plugins/Autoshield/` in that Valheim folder. `ValheimDir` defaults to `S:\Steam\steamapps\common\Valheim`.

## Compatibility

Built and tested against Valheim 1.0.20 with BepInEx 5.4.23 (BepInExPack Valheim 5.4.2351). Tested alongside *Equipment and Quick Slots*.

## License

[MIT](LICENSE)
