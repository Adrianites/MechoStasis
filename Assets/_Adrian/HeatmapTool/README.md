# Heatmap Tool

Records where players go and what they do (idle, walking, sprinting, airborne, jumps, falls, landings, plus any
events you add), then shows it as a heatmap in the Scene and Game views. It works in the editor and in builds, and
the data files move freely between them.

Self-contained: copy the `HeatmapTool` folder into any Unity 2021.3+ project. It works with the built-in pipeline,
URP and HDRP, and with any input system or player controller.

## Quick start

1. Select your player and choose **Tools > Heatmap > Add Tracker to Selected**. Put it on the object that has the
   `CharacterController` or `Collider`.
2. Press **Play** and move around. Stop.
3. Open **Tools > Heatmap > Heatmap Viewer**. The session is already loaded.

## Recording

`HeatmapTracker` samples the player 5 times a second by default and streams to disk every 10 seconds. It only
appends new data, so writing costs the same however long the session runs, and a crash loses at most the last flush.

| Detects | How |
|---|---|
| Grounded | `IHeatmapStateProvider` if your controller has one, otherwise `CharacterController.isGrounded`, otherwise a short sphere cast under the collider |
| Sprinting | `IHeatmapStateProvider.IsSprinting`, otherwise horizontal speed ≥ **Sprint Speed** |
| Jump / Fall / Land | Leaving the ground going up = Jump, otherwise Fall. Land stores the fall height as its value |

Set **Sprint Speed** a little below your controller's sprint speed. For exact results, implement the interface:

```csharp
using HeatmapTool;

public class PlayerController : MonoBehaviour, IHeatmapStateProvider
{
    public bool IsGrounded => controller.isGrounded;
    public bool IsSprinting => sprintHeld && moveInput.sqrMagnitude > 0.01f;
    // ...
}
```

## Custom events without code

In the tracker's **Custom Events** list, press **+** and fill in the entry:

1. **Object**: drag in the object with the script you want to watch. Leave it empty to use the player itself.
2. **Watch**: pick something from that object's scripts. The list shows:
   - **events**: UnityEvents and C# events. These record every time they fire, and a number they pass along (such as
     damage) becomes the event's value.
   - **values**: bool, number and enum fields or properties, public or private. Then choose **Record When**:
     - bools: *Becomes True*, *Becomes False*, *Changes*
     - numbers: *Changes*, *Increases*, *Decreases*, *Rises Above*, *Falls Below*, *Becomes*
     - enums: *Changes*, *Becomes*
3. **Record At**: where the event is placed, either the player or the watched object.
4. **Cooldown**: stops spam from values that change every frame.

Examples:
- `health` with *Decreases* records "Damaged", with the damage amount as the value.
- `isDead` with *Becomes True* records "Death".
- An `onCoinCollected` UnityEvent records every pickup.

The events appear in the viewer's Events list like Jump and Land. Watching starts on the first frame of a session,
after every script's Awake and Start, and stops when the session ends.

The tracker also has `RecordEvent(string)`, so any UnityEvent in the inspector, such as a Button's *On Click*, can
record an event by picking *HeatmapTracker > RecordEvent* and typing a name.

If a build uses **Managed Stripping Level** *Medium* or *High*, properties that nothing else uses can be stripped.
Watch fields, or keep stripping at *Minimal* or *Low*.

Custom events from code, from anywhere:

```csharp
Heatmap.LogEvent("Death", transform.position);
Heatmap.LogEvent("Checkpoint", checkpoint.position, value: checkpointIndex);
```

Other API: `Heatmap.RecordingAllowed = false` (privacy opt-out), `Heatmap.FlushAll()`,
`tracker.StartNewSession()` (e.g. per match), and `tracker.RecordEvent(name)` when tracking several players.

The inspector shows live status while playing. Use **Record In Builds** / **Development Builds Only** to control
when builds record.

## Where the data goes

`Application.persistentDataPath/Heatmaps`. For this project on Windows that is:

```
%USERPROFILE%\AppData\LocalLow\%PROJECT-NAME%\Heatmap\Heatmaps
```

On Windows, macOS and Linux the editor and builds share this folder, so builds you run on your own machine show up
in the viewer automatically. For a tester's build, ask them for that folder and drag the files (or the whole folder)
onto the viewer window.

**Save Builds Next To .exe** (viewer > *Recording*): builds save to a `Heatmaps` folder beside the game's executable
(beside the `.app` on macOS), so testers can zip it up with the game. The viewer then watches the `Heatmaps` folder of
your last build automatically. Editor recordings still go to persistentDataPath. If the game's folder can't be written
to (for example inside Program Files), the build falls back to persistentDataPath. Mobile, console and WebGL builds
always use persistentDataPath. The setting is stored in `Runtime/Resources/HeatmapSettings.asset`, which the window
creates the first time you tick it. A tracker's own *Custom Folder* still overrides it.

## Viewer

- **Sessions**: tick which sessions to include. *Current scene only* hides sessions from other levels. Use the `...`
  menu on a session to solo it, frame it, show its file, or remove it.
- **What to Show**: *Time Spent* (filter by Idle, Walking, Sprinting, Airborne; for example, show only where people
  sprint) or *Events* (Jump, Land, Fall, Death...). *Limit Time* restricts it to part of each session, such as the
  first two minutes.
- **Display**: Tiles, Cubes or Columns; cell size; *Flatten Heights* for single-floor maps; smoothing; colours;
  linear/log scale; *Hottest At Percentile* (stops the spawn point washing everything else out); *See Through Walls*.
- **Layers**: player paths coloured by movement state, event markers, and live updating while in Play mode.
- **Import / Export**: `.heatmap` (binary, one session per file), `.json` (many sessions in one readable file), and
  CSV export of raw samples or of the finished heatmap cells for spreadsheets.
- **Top-Down Map**: settings for the level picture saved with CSV exports (see below).

## Top-down map

Exporting either CSV also saves a top-down picture of the open level next to it, so a heatmap you draw from the CSV
can sit on top of the real map. Exporting `heatmap_cells.csv` writes:

| File | Contents |
|---|---|
| `heatmap_cells_map.png` | The level seen straight down. +Z is the top of the image, +X is the right |
| `heatmap_cells_map_bounds.csv` | One row: `min_x, max_x, min_z, max_z` (world metres the picture covers), size in pixels, pixels per metre |

The picture covers every shown session's samples and events plus *Padding*, so the same map fits both CSV exports.
*Hide Above* cuts away geometry more than that many metres above the highest floor players walked on, so roofs and
ceilings don't hide the floor. Turn off *Save With CSV Export* to skip it, or save a map on its own from **Export**
(optionally with the current heatmap, paths and markers drawn on). It renders the scene that is open, so open the
level the sessions were recorded in first.

Plotting a CSV over the map with Python:

```python
import pandas as pd
import matplotlib.pyplot as plt

cells = pd.read_csv("heatmap_cells.csv")
b = pd.read_csv("heatmap_cells_map_bounds.csv").iloc[0]

plt.imshow(plt.imread("heatmap_cells_map.png"), extent=[b.min_x, b.max_x, b.min_z, b.max_z])
plt.scatter(cells.x, cells.z, c=cells.iloc[:, 3], cmap="inferno", marker="s", s=10, alpha=0.7)
plt.show()
```

In Excel, use the PNG as the chart's plot-area fill and set the X axis to `min_x`–`max_x` and the Y axis to
`min_z`–`max_z`.

## File formats

| Format | Use |
|---|---|
| `.heatmap` | What the tracker writes. About 300 KB per hour of play. Can be read while still being written |
| `.json` | Sharing and merging many sessions. Import and export |
| `.csv` | Export only, for spreadsheets and other tools |

All of `HeatmapIO` (load, save, JSON, CSV) works in builds too, as does `HeatmapGrid` + `HeatmapMeshBuilder` +
`HeatmapView` if you want to show heatmaps in-game.

## Reusing in another project

Copy the `HeatmapTool` folder (or export it with *right-click > Export Package...*). It has its own assembly
definitions and no dependencies on this project.
