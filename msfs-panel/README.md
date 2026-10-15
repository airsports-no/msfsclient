# Airsports flight display: MSFS toolbar panel

A toolbar panel for MSFS 2020 and 2024 that shows the Airsports client's flight display inside the simulator: tracking
state with play/stop, start timer, training feedback, photos with Mark, and announcements. It has no logic of its own: it
connects to the client running on the same PC (`ws://localhost:47911/ws`, see `msfsclient.Core/Hud/HudServer.cs`) and uses
the same `hud.js` and `hud.css` as the client's page at http://localhost:47911/.

## What is in here

| Path | What |
|---|---|
| `airsports-flight-display.xml` | MSFS SDK project |
| `PackageDefinitions/` | Package definition and the Content Manager thumbnail |
| `PackageSources/InGamePanels/InGamePanel_Airsports.xml` | The toolbar entry and window (id, icon, default size and position) |
| `PackageSources/html_ui/InGamePanels/AirsportsPanel/` | The panel page (`ingame-ui` frame around `#airsports-hud`) |
| `PackageSources/html_ui/icons/toolbar/ICON_TOOLBAR_AIRSPORTS.svg` | Toolbar icon |
| `prebuilt/InGamePanels/InGamePanel_Airsports.spb` | The compiled panel definition (commit it) |
| `build-panel.ps1` | Builds the `.spb` with the SDK (once), assembles the package and the zip |

## Building

The package is plain files plus one compiled file, the `.spb`, which the SDK makes from `InGamePanel_Airsports.xml`. It
only changes when that XML changes (toolbar entry, default size), so it is built once and committed:

```powershell
./msfs-panel/build-panel.ps1 -Sdk     # needs the MSFS SDK; the SDK's package tool starts the simulator in the background
git add msfs-panel/prebuilt
```

After that, `./msfs-panel/build-panel.ps1 -Version 1.0.0` (or CI) assembles `dist/airsports-flight-display/` and
`dist/airsports-flight-display-<version>.zip` without the SDK. CI does this on every build when the `.spb` is committed,
and the client ships with the result (Profile > Toolbar panel in MSFS).

A package built with the MSFS 2020 SDK works in both simulators (MSFS 2024 loads 2020 packages). Built with the 2024 SDK,
check that MSFS 2020 loads it before relying on it there.

## Trying it in the simulator

1. Build the package, then copy `dist/airsports-flight-display` into the Community folder, or use Profile > Toolbar panel
   in MSFS in the client.
2. Start the client and the simulator; Airsports appears in the toolbar at the top of the screen.
3. Developer mode > Tools > Coherent GT Debugger shows the panel's page and console. Edits to `html_ui` files in the
   Community copy are picked up with Ctrl+Shift+R in the debugger, without rebuilding.

To check first: that the panel connects (the simulator's browser must open `ws://localhost:47911/ws`; the client accepts
`coui://` pages), that clicks reach the buttons, and the font sizes at your screen resolution.

## Distribution

- **In the client:** Profile > Toolbar panel in MSFS installs or updates it in every MSFS 2020/2024 install it finds (from
  each install's `UserCfg.opt`), also from the Microsoft Store version of the client.
- **flightsim.to:** upload `dist/airsports-flight-display-<version>.zip`. Pilots unzip it into their Community folder. The
  panel needs the Airsports client (Microsoft Store) running on the same PC.
