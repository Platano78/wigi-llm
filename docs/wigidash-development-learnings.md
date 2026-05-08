# WigiDash Widget Development — Hard-Won Learnings

Reference for future development on this repo. Captures what would be expensive to re-discover. **Read this before adding a new widget or modifying an existing one.**

---

## Project at-a-glance

- **Repo**: https://github.com/Platano78/wigi-llm (public, MIT)
- **Local path**: `/home/platano/project/wigi-llm`
- **Daily-driver widget**: LLMLauncherWidget (GUID `B8C9D0E1-F2A3-4567-8901-BCDEF1234567`)
- **Other widgets**: LLMStatsWidget, LLMBrainMonitorWidget (cockpit + autopilot game), plus 4 experimental (LLMControlCenter, LLMModelSelector, LLMRouterStatus, ClipboardAgent)
- **Shared helper**: `wigidash/src/Shared/GpuInfo.cs` (nvidia-smi VRAM detection, used by all C# widgets except Launcher)
- **Target framework**: **.NET Framework 4.7.2**, **C# LangVersion 5** — old enough that many modern C# features will silently fail to compile
- **Canvas sizes**: WidgetSize × 96 px per cell. 1×1 = 96×96, 2×1 = 96×192, 4×2 = 192×384 (rotated to 480×320 in BrainMonitor's case as fullscreen)

---

## Build / deploy workflow (WSL ↔ Windows)

The repo lives in WSL. msbuild lives on Windows. Bridging them has gotchas:

### Build

```bash
# 1. Stage to a Windows-accessible path (UNC paths break msbuild)
WIN_BUILD="/mnt/c/Users/<USER>/AppData/Local/Temp/wigi-llm-build/<WidgetName>"
mkdir -p "$WIN_BUILD/Properties"
cp wigidash/src/<WidgetName>/*.cs "$WIN_BUILD/"
cp wigidash/src/<WidgetName>/*.csproj "$WIN_BUILD/"
cp wigidash/src/<WidgetName>/*.bat "$WIN_BUILD/"
cp wigidash/src/<WidgetName>/Properties/*.cs "$WIN_BUILD/Properties/"
# Framework DLL — copy from any sibling widget folder that has it
cp wigidash/src/LLMLauncherWidget/WigiDashWidgetFramework.dll "$WIN_BUILD/"

# 2. Build via cmd.exe interop
cmd.exe /c "cd /d C:\\Users\\<USER>\\AppData\\Local\\Temp\\wigi-llm-build\\<WidgetName> && build.bat"
```

**Why staging is needed**: `cmd.exe /c "pushd \\wsl.localhost\..."` answers "UNC paths are not supported. Defaulting to Windows directory" — msbuild then runs from `C:\Windows` and can't find the project. The Z: drive auto-mapping is unreliable.

### Deploy

The deployed widget DLL is held open by WigiDash Manager when the widget is on a tile. **Cannot overwrite a locked DLL**:

```bash
DEPLOY="/mnt/c/Users/<USER>/AppData/Roaming/G.SKILL/WigiDashManager/Widgets/<GUID>"
cp "$WIN_BUILD/bin/Release/<GUID>.dll" "$DEPLOY/"  # fails: Permission denied
```

**Required dance**: user exits WigiDash Manager from the system tray (not just close the window — full exit) → deploy → reopen.

A new widget GUID has no deployed DLL yet, so first-time deploys go through without the exit dance. After the user adds the widget to a tile, future deploys need the exit.

### Build script convention

```batch
@echo off
echo Building <WidgetName>...
call "C:\Program Files\Microsoft Visual Studio\2022\Preview\Common7\Tools\VsDevCmd.bat" 2>nul
msbuild <WidgetName>.csproj /t:Build /p:Configuration=Release /v:minimal
echo.
echo Build completed with exit code: %ERRORLEVEL%
```

**Don't include `pause`** at the end — blocks cmd.exe interop. **Configuration=Release** for production; Debug builds work but ship larger PDBs.

### Deploy script convention

Each widget has its own `deploy.bat` that knows its GUID:

```batch
@echo off
set GUID=<WIDGET-GUID>
set WIDGETS=%APPDATA%\G.SKILL\WigiDashManager\Widgets
set TARGET=%WIDGETS%\%GUID%
if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "bin\Release\%GUID%.dll" "%TARGET%\"
copy /Y "bin\Release\%GUID%.pdb" "%TARGET%\"
copy /Y "icon.png" "%TARGET%\" 2>nul
```

---

## .NET Framework 4.7.2 / LangVersion 5 — what doesn't work

Pi and Codex both shipped code with these errors. **Verify every build, not every type-check.**

| Modern syntax | Problem | Fix |
|---|---|---|
| `volatile double` | `CS0677: a volatile field cannot be of the type 'double'` — illegal in any C# version | Drop `volatile`. CLR aligns 8-byte fields, reads are effectively atomic on x64. Worst case: one stale frame. |
| `double.TryParse(s, IFormatProvider, out v)` | 3-arg overload was added in .NET 7. Doesn't exist in 4.7.2. | Use 4-arg: `TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)` |
| `out var x` | LangVersion 5 doesn't support implicit-typed `out` | `int x; method(out x);` |
| `$"interpolated {value}"` | Requires C# 6+ | `string.Format("{0}", value)` or concat |
| `=>` expression-bodied members (`int Foo => 42;`) | Requires C# 6+ | Old-school getter blocks |
| `nameof(X)` | Requires C# 6+ | Hardcoded strings |

**Generic lambdas are fine** — `new Thread(() => DoStuff())` works in C# 5. Only the bullet items above are blocked.

---

## WigiDash framework gotchas

### GUID conventions (case-sensitive!)

For a widget with GUID `546C1005-E804-4AF5-996C-F3A3B922451F`:

```xml
<!-- csproj -->
<ProjectGuid>{546C1005-E804-4AF5-996C-F3A3B922451F}</ProjectGuid>   <!-- braces, uppercase -->
<AssemblyName>546C1005-E804-4AF5-996C-F3A3B922451F</AssemblyName>   <!-- no braces, uppercase -->
```

```csharp
// AssemblyInfo.cs
[assembly: Guid("546c1005-e804-4af5-996c-f3a3b922451f")]   // lowercase, hyphens, no braces
```

```csharp
// In WidgetBase.cs:
public Guid Guid { get { return new Guid(GetType().Assembly.GetName().Name); } }
// AssemblyName is the GUID string — Guid ctor parses it into the runtime Guid.
```

Mismatching cases will load the widget but it'll behave oddly or fail to associate with a tile.

### `ClickType.Double` doesn't fire reliably on touchscreen

Verified empirically: rapid double-taps on the WigiDash touchscreen come in as two `ClickType.Single` events, not one `ClickType.Double`. **Implement manual detection** if you need double-tap:

```csharp
private const int DoubleTapWindowMs = 350;
private DateTime _lastSingleTapTime = DateTime.MinValue;
private CancellationTokenSource _pendingSingleAction;

public void ClickEvent(ClickType ct, int x, int y) {
    if (ct == ClickType.Single) {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastSingleTapTime).TotalMilliseconds < DoubleTapWindowMs) {
            // Second Single within window → treat as double-tap
            _pendingSingleAction?.Cancel();
            HandleDoubleTap();
            _lastSingleTapTime = DateTime.MinValue;
            return;
        }
        _lastSingleTapTime = now;
        // First tap — defer the single-tap action by DoubleTapWindowMs so a follow-up can override
        _pendingSingleAction = new CancellationTokenSource();
        var token = _pendingSingleAction.Token;
        Task.Run(async () => {
            try { await Task.Delay(DoubleTapWindowMs, token); }
            catch (TaskCanceledException) { return; }
            HandleSingleTap();
        });
    }
}
```

Keep `if (ct == ClickType.Double) HandleDoubleTap();` as a free fallback for hardware that does fire it.

### Widget lifecycle methods

Implementing `IWidgetInstance`:
- **`ctor(parent, size, guid, resourcePath)`** — build state, draw initial frame, start poll thread
- **`ClickEvent(ClickType, x, y)`** — touch handler
- **`SwipeEvent(int direction)`** — 0=up, 1=down, 2=left, 3=right
- **`RequestUpdate()`** — host asking for a fresh paint; redraw and signal
- **`EnterSleep()` / `ExitSleep()`** — pause/resume your background work
- **`Dispose()`** — stop threads, dispose bitmap and mutex
- **`GetSettingsControl() → UserControl`** — return null if you don't have a settings page; otherwise a WPF UserControl. Requires `PresentationCore` + `PresentationFramework` + `WindowsBase` references.

### Settings persistence

```csharp
WidgetObject.WidgetManager.StoreSetting(this, "key", "value");
string val;
WidgetObject.WidgetManager.LoadSetting(this, "key", out val);
```

Keys are scoped to the widget instance (Guid). String values only — base64 binary if needed.

---

## Threading discipline (or: how to crash WigiDash Manager)

Every widget runs background threads (poll, animation). **Any unhandled exception on a background thread terminates the host process**, not just the widget. Several real crashes this session traced to missing `try/catch`.

### Mandatory pattern for background loops

```csharp
private void PollLoop() {
    while (_runPoll) {
        try {
            PollTick();
        } catch {
            // Never let an exception escape — kills the host.
            // Next tick retries.
        }
        Thread.Sleep(PollIntervalMs);
    }
}
```

Same applies to animation threads, deferred tasks, anything `Thread.Start`-ed or `Task.Run`-ed.

### Constructor must be defensive too

`new Bitmap(0, 0)` throws. Initial `DrawFrame()` from a half-initialized state can throw. Both can take down the host on widget load:

```csharp
public WidgetInstance(...) {
    int w = size.Width > 0 ? size.Width : 480;   // clamp
    int h = size.Height > 0 ? size.Height : 320;
    BitmapCurrent = new Bitmap(w, h, PixelFormat.Format16bppRgb565);
    try { DrawFrame(); } catch { }   // never throw out of ctor
    try { StartPoll(); } catch { }
}
```

### Multiple threads → mutex everything that touches the bitmap

```csharp
private readonly Mutex _drawingMutex = new Mutex();
private const int MutexTimeout = 100;

if (_drawingMutex.WaitOne(MutexTimeout)) {
    try { DrawFrame(); SignalUpdate(); }
    finally {
        try { _drawingMutex.ReleaseMutex(); } catch { }
    }
}
```

If poll thread + game thread both draw, **make one yield to the other**. In BrainMonitor: while game mode is on, poll thread skips its draw section so they don't fight.

### Lifecycle ops on threads must be idempotent

Rapid double-taps could spawn two animation threads if `StartGameMode` doesn't guard against an existing one:

```csharp
private readonly object _lifecycleLock = new object();

private void Start() {
    lock (_lifecycleLock) {
        if (_running) return; // already running
        _running = true;
        _thread = new Thread(...);
        _thread.Start();
    }
}

private void Stop() {
    Thread t;
    lock (_lifecycleLock) {
        if (!_running) return;
        _running = false;
        t = _thread;
        _thread = null;
    }
    // Join OUTSIDE the lock — otherwise Start can deadlock against a slow tick
    if (t != null) try { t.Join(800); } catch { }
}
```

### Snapshot fields read in tight loops

If thread A nulls `_game` while thread B is mid-iteration, NullReferenceException. **Snapshot to a local at the top of each tick**:

```csharp
private void GameTick() {
    GameMode game = _game;        // snapshot
    Bitmap bmp = BitmapCurrent;   // snapshot
    if (game == null || bmp == null) return;
    // Use `game` and `bmp` for the rest of the tick — even if _game is nulled
    // mid-iteration, our local reference stays valid.
}
```

### Animation FPS tradeoffs

- 30 FPS = 33ms frame = `WidgetUpdated` event fired ~30×/sec. Stresses GDI+ and the host's update dispatch.
- 20 FPS = 50ms frame = comfortable, still smooth-looking for game / particle visuals.
- 15 FPS = 67ms frame = fine for HUDs that don't need motion.

**Default to 20 FPS for animated widgets, 1-2s for static dashboards.**

---

## llama.cpp `/metrics` specifics

The router (port 8081) and standalone llama-server instances expose Prometheus-format metrics. **Several gotchas:**

### Router requires `?model=<name>`

```
GET http://127.0.0.1:8081/metrics
→ 400 {"error":{"code":400,"message":"model name is missing from the request"}}

GET http://127.0.0.1:8081/metrics?model=general-qwen36-35b
→ 200 (Prometheus text)
```

Standalone llama-server instances on per-model ports (e.g., 8083) accept `/metrics` without the query.

To get the per-model port, parse the loaded model's `--port` arg from `/v1/models` JSON. See `LLMBrainMonitorWidget/WidgetInstance.cs` `FetchModelsAsync()`.

### Parse line-by-line, skip `#` comments

Each metric appears 3× in the output:

```
# HELP llamacpp:predicted_tokens_seconds Average generation throughput in tokens/s.
# TYPE llamacpp:predicted_tokens_seconds gauge
llamacpp:predicted_tokens_seconds 40.97
```

A naive `IndexOf("llamacpp:predicted_tokens_seconds")` matches the `# HELP` line first. Splitting that line on whitespace, the last token is `tokens/s.` — `double.TryParse` returns false, you read 0.

**Correct pattern**:

```csharp
foreach (string rawLine in metricsText.Split('\n')) {
    string line = rawLine.Trim();
    if (line.Length == 0 || line[0] == '#') continue;
    if (!line.StartsWith("llamacpp:predicted_tokens_seconds ")) continue;  // trailing space matters
    // last whitespace-delimited token is the value
}
```

### Useful metrics on the router

| Metric | Type | Use |
|---|---|---|
| `llamacpp:predicted_tokens_seconds` | gauge | Average gen throughput (instantaneous-ish) |
| `llamacpp:tokens_predicted_total` | counter | Cumulative gen tokens (delta = tokens since last poll) |
| `llamacpp:prompt_tokens_seconds` | gauge | Prefill throughput |
| `llamacpp:prompt_tokens_total` | counter | Cumulative prefill tokens |
| `llamacpp:requests_processing` | gauge | Currently active requests |
| `llamacpp:requests_deferred` | gauge | Queued requests waiting for a slot |
| `llamacpp:n_busy_slots_per_decode` | counter | Avg slot utilization |
| `llamacpp:n_tokens_max` | counter | Largest context size observed |

### `/metrics` stalls during heavy inference

Single-threaded server: while a request is processing, `/metrics` requests can take >1s to respond. With a 1-second HttpClient timeout, polls during inference fail silently. **Token deltas arrive in a burst when inference completes**, not as smooth deltas during.

Implication for visualizations driven by token deltas: **rate-limit your spawn/effect rate**, or you'll get a wave of activity right after each generation. Use a clock-based gate, not a `dt`-accumulating one (the latter banks intervals while idle and bursts when debt arrives).

### `prompt_tokens_seconds` is "last value" between requests

The gauge holds whatever it computed at the end of the last request. Between requests, `requests_processing == 0` but the gauge is still showing a number. **Filter by `requests_processing > 0` if you want "is currently inferring."**

---

## Code patterns that worked

### MetricsClient as static `Fetch() → Snapshot`

Used in LLMStatsWidget, planned for LLMFingerprintWidget. Keep it stateless — caller polls on its own cadence:

```csharp
public class StatsSnapshot {
    public string ModelName;
    public double GenTokensPerSec;
    public double PromptTokensPerSec;
    public int RequestsProcessing;
    public bool Reachable;
}

public static class MetricsClient {
    public static StatsSnapshot Fetch() { /* HTTP + parse */ }
}
```

Reusable. No per-widget state pollution. Tests well in isolation.

### Helper components in their own files

BrainMonitor split rendering into `RingGauge.cs`, `VramBar.cs`, `Sparkline.cs`, `ParticleField.cs`, `HudPanel.cs`, `GameMode.cs`. Each is a static class or self-contained class with `Draw(Graphics, Rectangle, ...)` signature. Composable: `WidgetInstance.DrawFrame()` is mostly orchestration.

### Glass-pane HUD over a dynamic background

```csharp
// Background layer — dynamic data-driven (particles, etc.)
particleField.Draw(g, fullCanvas);

// HUD layer — semi-transparent gradient pane behind text/numbers
using (LinearGradientBrush pane = new LinearGradientBrush(
    bounds, Color.FromArgb(150, 18, 24, 36), Color.FromArgb(110, 10, 14, 22), 90f)) {
    g.FillRectangle(pane, bounds);
}
// Then HUD text on top — readable over any background
```

### Token deltas as visual driver

Beat a sin() loop. Drove the BrainMonitor particle field and game-mode enemy spawning. Real data → visceral feedback that the model is actually doing something.

### Color tier mapping for throughput

Used in stats widget and game mode. Visual at-a-glance feedback:

```csharp
static Color ColorForTps(double tps) {
    if (tps <= 0)   return Color.FromArgb(120, 120, 140);  // gray idle
    if (tps < 15)   return Color.FromArgb(255, 200, 60);   // yellow slow
    if (tps < 40)   return Color.FromArgb(0, 230, 255);    // cyan good
    return Color.FromArgb(80, 255, 140);                   // green great
}
```

### Multi-size widgets

```csharp
public List<WidgetSize> SupportedSizes {
    get { return new List<WidgetSize> { new WidgetSize(1, 1), new WidgetSize(2, 1) }; }
}
```

Then in DrawFrame, scale font sizes / layouts based on `BitmapCurrent.Width/Height`. Lets users pick how much screen real estate to give the widget.

---

## Anti-patterns / pitfalls

### "Burst on first match" parsers

```csharp
int idx = text.IndexOf("metric_name");  // matches the # HELP line first
```

Iterate all lines. Skip comments. Match by `StartsWith(prefix + " ")`.

### `dt`-accumulating rate limits

```csharp
_interval += dt;
while (_debt && _interval >= period) { spawn(); _debt--; _interval -= period; }
```

If `_debt == 0` for 5 seconds, `_interval = 5.0`. When debt finally arrives, the while loop drains 25 spawns at once. **Use a clock-based gate**:

```csharp
DateTime now = DateTime.UtcNow;
if (_debt && (now - _lastSpawn).TotalSeconds >= period) {
    spawn();
    _lastSpawn = now;
}
```

### `volatile double`

Doesn't compile. Drop `volatile`. CLR alignment + x64 atomicity makes it OK for UI display fields where occasional torn read = invisible.

### Naive trust of pi / model-generated code

Pi shipped 2 latent compile errors out of 3 fixes this session. Always run an actual build before declaring done. Type-check ≠ build success.

### Drawing without a mutex

Concurrent `Graphics.FromImage(bmp)` on the same Bitmap from two threads crashes GDI+. Use a mutex (or any lock) to serialize draws.

---

## Tool routing for this codebase

Tested in this session:

| Task | Tool that worked |
|---|---|
| Read 1100-line file | Native `Read` (within 2000-line limit) |
| Cross-file search "where is X used" | `grep -rn` via Bash |
| Surgical edit (10-50 lines) | Native `Edit` |
| New file 100-300 LoC | `bash heredoc` (Write tool blocked >100 LoC) |
| New file >300 LoC | `fork-pi` with `general-qwen36-35b`, then verify with build |
| Multi-file rename | Manual `git mv` + Edit |
| Building C# from WSL | cmd.exe interop staging to %TEMP% |

**fork-pi caveat**: ships latent compile errors. Always build after. Reliability flags are mandatory:

```bash
pi --print --provider llama --model general-qwen36-35b \
   --tools read,bash,edit,write \
   --no-extensions --no-skills --no-context-files --offline \
   "$SPEC"
```

---

## Repository state convention

- `master` is always green and deployable
- Each commit corresponds to one logical change with a detailed message (the why, not just the what)
- Commits include `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` when AI-assisted
- `docs/` contains plans (e.g., `inference-fingerprint-plan.md`) and learnings (this file)
- `.gitignore` excludes `bin/`, `obj/`, `*.dll`, `*.pdb`, IDE files

---

## Quick-start for a new widget

1. Pick a fresh GUID: `uuidgen | tr 'a-z' 'A-Z'`
2. Create `wigidash/src/<Name>Widget/` with these files (copy from `LLMStatsWidget` as the cleanest reference):
   - `<Name>Widget.csproj` (update GUID + AssemblyName + RootNamespace)
   - `Properties/AssemblyInfo.cs` (lowercase GUID)
   - `WidgetBase.cs` (Name, Description, SupportedSizes)
   - `Widget.cs` (Load/Unload/CreateInstance/GetWidgetPreview)
   - `WidgetInstanceBase.cs` (boilerplate)
   - `WidgetInstance.cs` (your logic)
   - `build.bat` (Release config, no pause)
   - `deploy.bat` (your GUID)
3. Reference `WigiDashWidgetFramework.dll` next to the csproj (gitignored, copy from a sibling widget when building)
4. If you need WPF (`UserControl` for settings), reference: `PresentationCore`, `PresentationFramework`, `WindowsBase`, `System.Xaml`
5. Build via the WSL→Windows staging dance documented above
6. Deploy to `%APPDATA%\G.SKILL\WigiDashManager\Widgets\<GUID>\`
7. Restart WigiDash Manager → widget should appear in the Add Widget list

---

## Session checkpoints (where to dig for examples)

| Need an example of... | Look at |
|---|---|
| Config-driven (buttons.json) widget | `LLMLauncherWidget/` |
| Minimal stats display (1×1 / 2×1) | `LLMStatsWidget/` |
| Animation thread + game logic | `LLMBrainMonitorWidget/GameMode.cs` + `WidgetInstance.cs` GameLoop |
| Composable rendering helpers | `LLMBrainMonitorWidget/{RingGauge,VramBar,Sparkline,HudPanel,ParticleField}.cs` |
| Manual double-tap detection | `LLMBrainMonitorWidget/WidgetInstance.cs` `ClickEvent` |
| Settings persistence per model | (planned) `LLMFingerprintWidget/HistogramSerializer.cs` |
| llama.cpp /metrics polling | `LLMBrainMonitorWidget/WidgetInstance.cs` `FetchMetricsAsync` |
| GpuInfo / nvidia-smi reading | `wigidash/src/Shared/GpuInfo.cs` |

---

## MCP Gateway (127.0.0.1:8090) — data source findings

**Date probed**: 2026-05-08 (Neural Nexus widget development)

### Reachability

| Endpoint | Response | Notes |
|---|---|---|
| `GET /` | `401 {"error":"Unauthorized"}` | Needs auth token |
| `GET /health` | **200** — full JSON with server topology | **Primary data source for Neural Nexus** |
| `GET /metrics` | `401 {"error":"Unauthorized"}` | Needs auth token |

### `/health` response schema

```json
{
  "status": "ok",
  "uptime_seconds": 65621,
  "servers": {
    "serena": { "status": "connected", "tools_count": 21 },
    "context7": { "status": "connected", "tools_count": 2 },
    "stitch": { "status": "disconnected", "error": "can't resolve reference...", "tools_count": 0 },
    "unity-mcp": { "status": "disconnected", "error": "Connection timeout", "tools_count": 0 }
  },
  "total_tools": 296
}
```

- `status`: `"ok"` or other error string
- `uptime_seconds`: integer
- `servers`: dict of `{server_name: {status, tools_count, error?}}`
- `status` values: `"connected"` or `"disconnected"`
- `tools_count`: integer (0 for disconnected)
- `error`: optional string (present on disconnected servers)

### Neural Nexus widget usage

The Neural Nexus widget (GUID `48B421E2-91C8-4B92-9CF8-F8E6C9BDACBE`) uses `/health` as its primary data source:
- Center node = "MCP Gateway" itself
- Peripheral nodes = each server from `servers` dict
- Health coloring: green (connected + polled ≤10s), amber (stale 10-30s), red (30s+ or 3+ consecutive failures)
- Poll interval: 2 seconds

### `claude_statusline.json` — NOT FOUND

No `/dev/shm/claude_statusline.json` or `~/.claude/*statusline*.json` exists. The `statusline.py` Python script exists at `~/.claude/statusline.py` with a `statusline_config.toml` for MCP display rules, but no JSON output file is generated. The PRD for Neural Nexus assumed this file exists — it does not. The `/health` endpoint is the correct data source.

---

## Final note

**Always build and deploy and visually verify before declaring a feature done.** Type-checks pass on illegal `volatile double`. Pi reports `was_truncated: false` on truncated output. The actual touchscreen + actual router + actual user-tap is the only ground truth.
