# HISA - New Eden. At a Glance.

**Haakario Interstellar Survey Authority**

### EVE Online Intel, Maps & Alerts

**A fast, lightweight, open-source intelligence and mapping companion for EVE Online.**

New Eden is a big place. Keeping track of what's happening around you shouldn't mean juggling a dozen windows or sacrificing your system's performance.

**HISA** brings interactive maps, local intel reports, system activity, character tracking, wormhole connections, jump networks, alerts, and mining tools together in one clean, responsive desktop app.

Whether you're watching for hostiles, planning a route, checking what's happening nearby, or managing mining operations, HISA puts useful information where you need it - without getting in your way.

### Built to be useful. Not in your way.

The goal has always been simple: **make something practical, fast, and lightweight.** Clean map rendering, configurable overlays, and efficient refreshes help you keep an eye on New Eden without needless clutter or overhead. Because a 2D map really shouldn't be trying to mine Bitcoin with your GPU. :P

HISA is actively used and developed. Bug reports, weird edge cases, and suggestions are welcome!

**Available for Windows, Linux, and macOS**, with compatibility depending on your setup.

**[Download the latest release](https://github.com/Crytopox/HISA/releases/latest)** · **[Join the Discord](https://discord.gg/ByVCvC6UY9)** · **[Other EVE tools](https://haakario.com/)**

## Features

### Interactive EVE Maps
- Universe, region overview, and individual region map views
- Custom, combined, and editable map layouts
- Built-in map editor for layout creation and adjustments

### Fast and Lightweight
- Responsive large maps, overlays, and live data
- Cached refreshes to avoid unnecessary requests
- Clean map rendering focused on readability and speed
- Debug telemetry to track ESI usage and refresh behavior

### Map Visuals and Overlays
- Map color modes and indicators for security, regions, constellations, star classes, nullsec true-sec, A0 stars, system activity, intel hostiles, character presence, jump range, wormholes, incursions, storms, ice belts, Jove Observatories, SOV upgrades, and more
- Activity badges for jumps, ship kills, pod kills, and NPC kills
- Configurable hover information with fields and icons

### Intel and Killmail Tools
- Local intel chat log parsing
- Hostile scoring and icons directly on the map
- Intel overlay cards with report details
- zKillmail integration for recent killmail overlays

### Character Tracking
- Local character location tracking
- Character names, counts, and markers displayed on the map

### Fleet Boost Tracker
- Click **Start tracking** to count new pilot declarations across Shield, Armour, Info, and Skirmish; **Reset tracking** starts a fresh session for that channel.
- Each pilot's latest charge post replaces their previous charges. Mindlink declarations (`ML`, `+ML`, `mindlink`) remain attached to that pilot until cleared, removed, or reset.
- View reporting pilots by clicking a charge; remove a pilot from all charge counts when needed.

### Live Data Integrations
- Live Thera / Turnur wormhole data
- Live incursion and Metaliminal storm data
- ESI public data for system kills and jumps

### SOV, Network, and Jump Tools
- SOV upgrade import, filtering, and map display
- Ansiblex network links and overlays
- Jump range overlays and light-year coverage
- Route planning with route leg summaries
- Jump range calculator

### Mining Stats / Dashboard
*Optional: enable in Settings > Preferences > Mining Logs.*

Read local game logs to display live mining session information:
- Regular yield and critical bonus yield
- Waste
- Mining rate
- Estimated value and ISK rate

Review current-session or recent-range stats, aggregate totals, and a compact live overlay.

### Mining Sites Tracker
Track SOV prospecting array sites and mark them as **Cleared** or **Missing**.

- **Cleared:** reminders based on the upgrade's respawn timer
- **Missing:** a configurable reminder to check whether the site has respawned

Pair the tracker with **Alert Settings** and the **Mining site ready** event type.

## Probe Formation Designer

Open **Tools > Probe Formation Designer** to build a local library of scanning
formations. The probe table shows every probe's XYZ coordinates and scan radius.
Edit cells directly; Enter or leaving a cell applies the value, and Escape cancels.
Coordinates use kilometers or AU, while scan radii use AU.

Click a probe in the table or 3D view to select it. Ctrl-click toggles selection;
Shift-click adds probes in the view or selects a range in the table. Drag empty
viewport space to select a group. Drag a selected probe to move the group in the
view plane, or use its colored XYZ handles for constrained movement. Escape
cancels a drag. Right-drag orbits, middle-drag pans, and the wheel zooms.
Enable **Snap to nearest integer** to snap mouse movement to whole values in the
current coordinate units (km or AU). The primary probe snaps while selected probes
keep their relative spacing; axis handles snap only the axis being moved.
**Center centroid at origin** moves the whole formation without changing its shape.
**Set all probes to 0** places every probe at the origin. Both actions preserve scan
radii and support undo.

The Move, Rotate, Scale, and Radius tabs apply to the selected probes, with
undo/redo. Rotation and scaling default to the selection center. **Save library**
persists your presets in HISA; JSON import/export shares them independently of EVE.

Select **Send to EVE…** for the guided **Formations → Accounts → Review → Results**
workflow. The current formation starts selected. Choose exact profile/account
files on the Accounts step, then resolve name conflicts inline during Review.
Account cards show linked character names and compact profile checkboxes. Use
**Inspect** for the Characters, Formations, and File detail tabs; full destination
paths, aliases, and backups are in File. Search is always available, with server
and profile filters and discovery tools available separately.
Close all EVE clients before applying. Results show each file's outcome and
backup location; retrying failed targets creates a fresh review. Existing account formations and unrelated settings are
preserved. Every write creates a verified backup in `.hisa-probe-backups` beside
the account file; the account panel can restore these backups, including when
the current settings file is corrupted. Restore replaces the complete settings
file and first backs up its current contents.

Windows settings and common Steam/Proton locations are discovered automatically.
Use **Locations…** for custom Steam libraries, Wine prefixes, settings profiles,
or an individual `core_user_*.dat` account file. Accounts are grouped by server
and account ID, with each destination profile/file selected separately.

Sibling `core_char_<id>.dat` filenames identify local characters. Public ESI
lookups resolve Tranquility names; CCP's image service supplies portraits, with
cached names and placeholders available offline. Other servers keep their own
identity namespace and never silently receive Tranquility names. No ESI login
is required. **Link characters…** lets you confirm and remember character labels
for an account group, including entering an ID manually. These are local labels,
not automatic ownership detection, and linking never selects destination files.
Character settings files are never edited. Unsupported or malformed binary
settings data blocks writes.

Implementation references and codec licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Screenshots

![Preview 1](https://i.imgur.com/LUIK7QL.png)

![Preview 2](https://i.imgur.com/rn0hbyG.png)

![Preview 3](https://i.imgur.com/XHqVRdr.png)

![Preview 4](https://i.imgur.com/IjNrxJX.png)

![Preview 5](https://i.imgur.com/huirzHI.png)

![Preview 6](https://i.imgur.com/4VCX2im.png)

![Preview 7](https://i.imgur.com/oP3uutk.png)

![Preview 8](https://i.imgur.com/DjzUZLH.png)

![Preview 9](https://i.imgur.com/MGrLQHN.png)

![Preview 10](https://i.imgur.com/3QSFQS6.png)

![Preview 11](https://i.imgur.com/EG5zIFK.png)

## Linux support

HISA ships Linux builds for `linux-x64`, `linux-arm64`, and `linux-musl-x64`.
In practice, the `linux-x64` build should run without issues on most modern
glibc-based distros such as Ubuntu, Linux Mint, Debian, Fedora, Pop!_OS,
openSUSE, Arch, EndeavourOS, and CachyOS. Use `linux-musl-x64` for musl-based
distros such as Alpine.

Releases are self-contained, so you do not need to install .NET separately.
HISA uses .NET 10 and Avalonia UI, so Linux systems may still need common
desktop libraries depending on the distro, typically X11/Wayland, fontconfig,
DBus, and OpenGL/Mesa-related packages.

For Avalonia UI dependencies for Linux:
https://docs.avaloniaui.net/docs/supported-platforms

## macOS support

HISA ships self-contained macOS builds for Intel Macs (`osx-x64`) and Apple
Silicon Macs (`osx-arm64`). No separate .NET installation is required.

The release archives contain an unsigned standalone executable, rather than a
signed `.app` bundle. macOS may therefore require you to explicitly allow the
app the first time you open it.

## Publishing a release

Releases are built manually from the current `main` branch by GitHub Actions. The
release workflow builds the solution, runs the test suite, publishes the Windows,
Linux, and macOS packages, generates SHA-256 checksums and signed build
provenance, and attaches the resulting files to a GitHub Release.

To publish a release:

1. Update `<Version>` in `src/Hisa.App/Hisa.App.csproj`.
2. Commit and push the release-ready code to `main`.
3. Open the repository's **Actions** tab on GitHub.
4. Select **Release**, click **Run workflow**, and run it.

The workflow checks out `main` explicitly and creates a matching release tag,
such as `v1.2.0` for `<Version>1.2.0</Version>`. Local publish scripts remain
available in `build/` for development checks, but published release files are
produced by GitHub Actions.

After downloading a release package, its provenance can be verified with the
GitHub CLI:

```powershell
gh attestation verify HISA-win-x64-v1.2.0.zip --repo Crytopox/HISA
```

## Building locally

The repository includes the trimmed `src/Hisa.App/Data/eve-hk-sde.db` database
required by HISA. A normal source checkout is sufficient for local development
and publishing.

To regenerate or refresh that database, build the full SDE SQLite database with
[Crytopox/HKSDEImporter](https://github.com/Crytopox/HKSDEImporter), place a copy
at `src/Hisa.App/Data/eve-hk-sde.db`, then run the HISA-specific SQLite trimming
script:

```powershell
sqlite3 src/Hisa.App/Data/eve-hk-sde.db ".read build/trim-eve-hk-sde.sql"
sqlite3 src/Hisa.App/Data/eve-hk-sde.db "VACUUM;"
```

Use the PowerShell publish scripts when building release archives locally:

```powershell
./build/publish-windows.ps1
./build/publish-linux.ps1
./build/publish-macos.ps1
```

The Windows script produces the `win-x64` ZIP archive. The Linux script produces
`linux-x64`, `linux-arm64`, and `linux-musl-x64` tar archives. The macOS script
produces `osx-x64` and `osx-arm64` tar archives. Output is written under
`build/releases/`.
