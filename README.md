# Lane Shifter

A Cities Skylines 1 mod that lets you shift individual road lanes laterally on any placed network segment.

## Features
- Click any road segment to open the Lane Shifter panel
- Hover a lane row to highlight that lane in-world (green overlay)
- Type an exact offset value directly into the field, or use the ±0.5 / ±1 preset buttons
- Reset any lane to 0 with the **0** button
- Shifts are saved with your map and restored on reload
- Right-click or Escape closes the panel and deselects the tool
- Automatically integrates with **UnifiedUI** if installed; falls back to a standalone toolbar button

## Building
Requires the Cities Skylines managed DLLs set as `CS_MANAGED` env var.
Optionally drop `UnifiedUILib.dll` into `lib/` before building to compile with UUI support.

The GitHub Actions workflow handles everything automatically on push.

## Installing
Copy `LaneShifter.dll` to your Cities Skylines `Mods` folder (usually `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/Addons/Mods/LaneShifter/`).
