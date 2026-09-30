# JonShift — Cities: Skylines 1 Lane Offset Mod

Shift individual lanes left or right on any placed road segment. Changes are
saved with the map so offsets persist across save/load cycles.

## Features
- Click the **🔄 Lane Shift** button (top-left of screen) to activate the tool
- Click any road segment to open the lane editor panel
- Each lane shows its type, base position, and current offset
- Buttons: **-½ / -¼ / +¼ / +½** to nudge a lane, **R** to reset it
- Offset is clamped to ±8 m
- Data saved per-map via `ISerializableData` (XML, stored inside the `.crp` save)
- Press **Esc** to deactivate the tool

## Building locally
1. Copy these DLLs from `Cities_Data/Managed/` into the `lib/` folder:
   - `Assembly-CSharp.dll`
   - `ColossalManaged.dll`
   - `ICities.dll`
   - `UnityEngine.dll`
   - `UnityEngine.UI.dll`
   - `0Harmony.dll` (copy from an existing Harmony 2.x mod in your local mods folder)
2. Run: `dotnet build JonShift.csproj -c Release`
3. Copy `bin/Release/net472/JonShift.dll` to your CS1 local mods folder

## GitHub Actions
The `build.yml` workflow builds automatically on every push to `main`.
The compiled DLL is uploaded as the **LaneShift-Mod-DLL** artifact.

> **Important:** `0Harmony.dll` must be in `lib/` for CI to build.
> The game ships its own Harmony 2 (via the Harmony mod); just copy `0Harmony.dll`
> from `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/Mods/` or from a mod that
> includes it.

## Architecture
| File | Purpose |
|---|---|
| `LaneShiftMod.cs` | `IUserMod` + `ILoadingExtension` entry points, Harmony wiring |
| `LaneShiftManager.cs` | Stores shift dictionary; applies bezier shifts; serialize/deserialize |
| `LaneShiftPatch.cs` | `NetSegment.UpdateLanes` Harmony postfix |
| `LaneShiftTool.cs` | `DefaultTool` subclass; raycast hover + click |
| `LaneShiftPanel.cs` | Floating `UIPanel` with per-lane shift buttons |
| `SerializableDataExtension.cs` | Hooks `OnSaveData` / `OnLoadData` |

## How the shift works
After the game calls `NetSegment.UpdateLanes` and writes each lane's Bezier,
the Harmony postfix calls `LaneShiftManager.ApplyShifts`. For every lane that
has a non-zero offset, the bezier's four control points are moved perpendicular
to the road direction by the stored amount (positive = right, negative = left).
