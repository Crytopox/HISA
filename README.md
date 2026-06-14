# HISA - Haakario Interstellar Survey Authority - A fast, Lightweight Intel Map for New Eden

An open source EVE Online intel map tool I’ve been working on, maybe you will find it useful.

The goal was to make something **practical**: a map that helps you quickly see what is going on, track useful intel, check activity, follow wormholes/incursions/storms, plan jumps, and keep an eye on characters without the app getting in your way.

But most importantly, all of that while staying as lightweight and fast as possible, because a 2D map should not be trying to mine Bitcoin with your GPU.

This is the first full release, so most of the core features and important pieces are already in place buuut, **expect a few issues here and there** (hopefully only minor ones xP)

I’m daily driving HISA myself, so anything I run into will be fixed as quickly as I can. If you find bugs, weird behavior, edge cases, or have suggestions, feel free to share them and I’ll be happy to look into them. 

**Available for both Windows and Linux**, but expect compatibility to depend a bit on your distro/setup. 

Go to releases to download the latest version.

Join the Official Discord for updates, feedback, and bug reports:  
https://discord.gg/ByVCvC6UY9

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


## Publishing a release

Releases are built manually from the current `main` branch by GitHub Actions. The
release workflow builds the solution, runs the test suite, publishes the Windows
and Linux packages, generates SHA-256 checksums and signed build provenance, and
attaches the resulting files to a GitHub Release.

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
```

The Windows script produces the `win-x64` ZIP archive. The Linux script produces
`linux-x64`, `linux-arm64`, and `linux-musl-x64` tar archives. Output is written
under `build/releases/`.

## Main Features
### Interactive EVE Maps
- Universe, region overview, and individual region map views
- Custom, combined, and editable map layouts
- Built in map editor for layout creation and adjustments

### Fast and Lightweight
- Built to stay as responsive as possible while handling large maps, overlays, and live data
- Cached refreshes to avoid unnecessary requests
- Clean map rendering focused on readability and speed
- Debug telemetry to help track ESI usage and refresh behavior

### Map Visuals and Overlays
- Multiple map color modes and indicators for security, region, constellation, star class, nullsec true-sec, A0 stars, system activity, intel hostiles, character presence, jump range, wormholes, incursions, storms, ice belts, Jove Observatories, SOV upgrades, and more
- Activity badges for jumps, ship kills, pod kills, and NPC kills
- Hover info box with configurable fields and icons, so you can quickly check what matters without opening extra windows

### Intel and Killmail Tools
- Local intel chat log parsing
- Hostile scoring and hostile icons shown directly on the map
- Intel overlay cards with report details
- zKillmail integration for recent killmail overlays

### Character Tracking
- Local character location tracking
- Character names, counts, and markers shown on the map

### Live Data Integrations
- Live Thera / Turnur wormhole data
- Live incursion and Metaliminal storm data
- ESI public data for system kills and jumps

### SOV, Network, and Jump Tools
- SOV upgrades import, filtering, and map display
- Ansiblex network links and overlays
- Jump range overlays and light-year coverage
- Route planning with route leg summaries
- Jump range calculator
- A few more in the plans....

## Image previews
![Preview1](https://i.imgur.com/XDrbQEh.png)
---
![Preview2](https://i.imgur.com/mfQaxLM.png)
---
![Preview3](https://i.imgur.com/uksm640.png)
---
![Preview4](https://i.imgur.com/AjQowco.png)
---
![Preview5](https://i.imgur.com/lYkn0p2.png)
