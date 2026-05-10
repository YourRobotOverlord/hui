<img width="1080" height="690" alt="Screenshot 2026-05-04 173747" src="https://github.com/user-attachments/assets/49dedcfb-5a66-4474-b037-31b26e22d66f" />

# hui

`hui` syncs Philips Hue entertainment lights to system audio on Windows. Use it either from the interactive Terminal.Gui app or from the CLI.

Light updates use the **Hue Entertainment API** DTLS stream on UDP `2100`. Bridge REST calls are used for pairing, area discovery, and starting or stopping entertainment mode.

## Installation

### Install as a global dotnet tool

Clone the repo, then pack and install:

```powershell
git clone https://github.com/YourRobotOverlord/hui.git
cd hui
dotnet pack -c Release
dotnet tool install --global --add-source ./bin/Release hui
```

After installation, run `hui` from anywhere:

```powershell
hui run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom
```

To update after pulling new changes:

```powershell
dotnet pack -c Release
dotnet tool update --global --add-source ./bin/Release hui
```

To uninstall:

```powershell
dotnet tool uninstall --global hui
```

## Requirements

- Windows machine with .NET 10 SDK (Linux version coming)
- Hue Bridge on the local network
- Entertainment area created in the Hue app
- Lights assigned to that entertainment area

## Quick start

### 1. Pair with the bridge

Press the link button on the Hue Bridge, then run:

```powershell
dotnet run -- pair --bridge 192.168.1.20
```

This requests and saves:

- **App key** for authenticated Hue API calls
- **Client key** for the Hue Entertainment DTLS connection

### 2. List entertainment areas

```powershell
dotnet run -- list-areas --bridge 192.168.1.20 --app-key YOUR_APP_KEY
```

This prints area IDs, names, status, and channel positions.

### 3. List audio output devices

```powershell
dotnet run -- list-devices
```

Use the `--device-index` value from this list if you do not want the default Windows playback device.

## Interactive app

Launch the Terminal.Gui interface:

```powershell
dotnet run -- ui
```

Running the app with no arguments also opens the UI.

### UI shortcuts

- `F1`: open the in-app `README.md` viewer
- `F3`: open bridge, audio, and app-exit lighting settings
- `F5`: start or stop lighting
- `n` / `p`: next or previous lighting mode
- `b` / `B`: brightness down or up for the current mode
- `s` / `S`: sensitivity down or up for the current mode

Use `F3` to edit bridge connection, device selection, FPS, and app-exit lighting behavior. Mode-specific settings are edited in the main **Mode Control** pane and are saved immediately.

## CLI mode

Run live audio sync directly from the command line:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area YOUR_AREA_ID
```

Example with common tuning options:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --device-index 1 --fps 40 --sensitivity 2.0 --brightness 0.8
```

### Mode examples

Cycle-strobe:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --mode cycle-strobe --brightness 1.0
```

Sparkle:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --mode sparkle --sensitivity 2.2 --brightness 0.9
```

Wave-travel:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --mode wave-travel --sensitivity 1.9 --brightness 0.95
```

Ambient-drift:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --mode ambient-drift --sensitivity 1.4 --brightness 0.55
```

Beat-pulse:

```powershell
dotnet run -- run --bridge 192.168.1.20 --app-key YOUR_APP_KEY --client-key YOUR_CLIENT_KEY --area LivingRoom --mode beat-pulse --sensitivity 1.9 --brightness 1.0
```

## CLI options

| Option | Required | Description |
|---|---|---|
| `--bridge` | No* | Hue Bridge IP address or hostname. |
| `--app-key` | No* | Hue application key used for authenticated bridge API calls. |
| `--client-key` | No* | Hue entertainment client key used as the PSK for the DTLS stream. |
| `--area` | No* | Entertainment area ID or exact entertainment area name. |
| `--device-index` | No | Windows audio device index from `list-devices`. If omitted, the default multimedia playback device is used. |
| `--fps` | No | Stream frame rate. Accepts `1-60`. Default `30`. |
| `--mode` | No | Lighting mode: `audio-reactive`, `cycle-strobe`, `sparkle`, `wave-travel`, `ambient-drift`, or `beat-pulse`. Default `audio-reactive`. |
| `--cycle-seconds` | No | Full hue cycle time in `cycle-strobe`. Accepts values `>= 0.1`. Default `6`. |
| `--wave-seconds` | No | Wave travel time in `wave-travel`. Accepts values `>= 0.1`. Default `1.6`. |
| `--sensitivity` | No | In `audio-reactive`, acts as audio gain. In `cycle-strobe`, `sparkle`, `wave-travel`, and `beat-pulse`, higher values make transient-triggered effects fire more easily. Accepts values `>= 0.01`. Default `1.75`. |
| `--brightness` | No | Maximum brightness cap for streamed colors. In `cycle-strobe` and `beat-pulse`, this directly controls flash or pulse intensity. Accepts `0-1`. Default `1`. |

## Behavior notes

- `pair` saves the bridge address, app key, and client key to the app config file.
- `--bridge`, `--app-key`, `--client-key`, and `--area` are optional if they are already saved in the config file (e.g. after running `pair` or using the UI). If any are missing from both the CLI and config, the app exits with an error.
- `--area` accepts either the exact area ID or the exact area name.
- `--brightness` limits maximum streamed brightness from `0` to `1`.
- `--fps` controls how often frames are pushed to the bridge. `30-50` is a practical range.
- `cycle-strobe` sweeps across the configured warm and cool hue range and back independently of the music. In this mode, `--brightness` controls flash intensity and `--sensitivity` controls how easily detected transients trigger flashes.
- `sparkle` keeps a dim base wash and overlays transient-triggered random white sparkles. In this mode, higher `--sensitivity` makes sparkles trigger more easily.
- In the UI, `sparkle` also exposes a configurable sparkle color picker for transient sparkle flashes.
- `wave-travel` launches alternating left-to-right and right-to-left waves on detected transients. `--brightness` controls wave intensity.
- `ambient-drift` slowly morphs between the configured hue endpoints while audio only nudges drift speed and brightness.
- `beat-pulse` launches a full-area pulse on detected beats with fast attack and smooth decay. In this mode, `--sensitivity` controls beat detection threshold and `--brightness` controls pulse intensity.
- The UI also includes `split-strobe`, which randomly splits lights into bass and treble groups with independent colors plus configurable attack, decay, and background levels.
- When the UI exits while lights are running, `hui` can either send a blackout frame or set the whole entertainment area to a configured solid color before stopping the stream.

<img width="320" height="320" alt="output" src="https://github.com/user-attachments/assets/2daeb89d-c078-4dc7-ae85-4601ca4a69c2" />![Uploading Screenshot 2026-05-04 173523.png…]()
