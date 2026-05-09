# WigiDash Widget Development — Hard-Won Learnings

Reference for future development on this repo. Captures what would be expensive to re-discover. **Read this before adding a new widget or modifying an existing one.**

---

## Project at-a-glance

- **Repo**: https://github.com/Platano78/wigi-llm (public, MIT)
- **Local path**: `/home/platano/project/wigi-llm`
- **Daily-driver widget**: LLMLauncherWidget (GUID `B8C9D0E1-F2A3-4567-8901-BCDEF1234567`)
- **LLM-server widgets**: LLMStatsWidget, LLMStatusWidget, LLMBrainMonitorWidget (cockpit + autopilot game), LLMControlCenter, LLMModelSelector, LLMRouterStatus
- **MCP / Claude Code widgets**: MCPPulseWidget (canonical MCP viz — topology + activity), ContextMonitorWidget (Claude Code burn-rate), IntegrationHealthWidget (service start/stop), ControlPanelWidget (action buttons)
- **Other**: ClipboardAgentWidget
- **Shared helpers** (`wigidash/src/Shared/`):
  - `GpuInfo.cs` — nvidia-smi VRAM detection (local + remote)
  - `WslPaths.cs` — WSL POSIX → Windows UNC translation, env-var configurable
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
| `const T = SomeMethod(...)` | C# 5 `const` requires a compile-time constant; method calls aren't allowed | Use `static readonly` instead. Behaviorally equivalent for module-scoped values; computed once at type init. |
| Auto-property initializers (`int X { get; set; } = 0;`) | Requires C# 6+ | Initialize in constructor |
| Null-conditional (`obj?.Foo`) | Requires C# 6+ | `obj != null ? obj.Foo : default` |

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

## MCP data sources — what works, what doesn't

**Date probed**: 2026-05-08 (Neural Nexus → MCPPulse merge)

There are two complementary signals to draw on for MCP visualization. Pick the right one for the widget you're building.

### MCP Gateway HTTP (`127.0.0.1:8090`) — topology / health

| Endpoint | Response | Notes |
|---|---|---|
| `GET /` | `401 {"error":"Unauthorized"}` | Needs auth token |
| `GET /health` | **200** — full JSON with server topology | **Only public endpoint.** Use this for state. |
| `GET /metrics` | `401` | Needs auth |
| `GET /events` `/calls` `/stream` `/sse` `/v1/*` | `401` | Probed 2026-05-08; all 401. Endpoints exist but auth-gated, so widgets can't use them today. |

`/health` response schema:

```json
{
  "status": "ok",
  "uptime_seconds": 65621,
  "servers": {
    "serena":   { "status": "connected",    "tools_count": 21 },
    "context7": { "status": "connected",    "tools_count": 2 },
    "stitch":   { "status": "disconnected", "error": "can't resolve reference...", "tools_count": 0 }
  },
  "total_tools": 296
}
```

`status` values: `"connected"` or `"disconnected"`. `error` is optional, present on disconnected servers.

### MCP log file (`~/.claude/logs/mcp.log`) — activity / tool calls

JSONL, one event per line. Schema (verified 2026-05-08):

```json
{"tool_name":"server::tool","server":"<name>","tool":"<name>","success":true,"duration":22,"timestamp":"2026-05-08T10:07:00-04:00"}
```

The `server` field maps directly to the keys in `/health`'s `servers` dict — that's the join key for unified topology+activity widgets. Tail with last-position tracking; only read new bytes per poll.

From Windows side, the file is at `\\wsl.localhost\<distro>\home\<user>\.claude\logs\mcp.log` — use `WslPaths.UnderHome(".claude/logs/mcp.log")` so it works for any user.

### Dead sources — don't use

- **`http://127.0.0.1:3457/api/stats`** — old MCPNeuralPulse data source. Returns HTTP 000 (connection refused) as of 2026-05-08. The custom stats server it depended on is gone.
- **`/dev/shm/claude_statusline.json`** and **`~/.claude/*statusline*.json`** — never existed. The `statusline.py` script exists with a config but doesn't generate a JSON output file. Several PRDs assumed this file existed; they were wrong.

### MCPPulse widget usage

MCPPulse (GUID `82CE97C3-2CB7-4658-8433-ED4300721E2F`) is the canonical MCP visualization, replacing the now-retired Nexus / MCPNeuralPulse / MCPHealth trio:
- **Topology layer**: polls `/health` every 2s, builds dynamic node list (one per server)
- **Activity layer**: tails `mcp.log` every 200ms, fires beam from `event.server` node → center on each call
- Health coloring: green (connected + polled ≤10s), amber (stale 10-30s), red (30s+ or 3+ consecutive failures)
- Three render modes via double-tap: full / topology-only / activity-only
- Dual-thread architecture with shared mutex on the bitmap (poll + tail threads, both with top-level try/catch)

---

## Shared/WslPaths.cs — WSL path translation

WSL widgets often read files from the Linux side. The Windows `\\wsl.localhost\<distro>\<path>` UNC form works from .NET `StreamReader`, but baking the distro and user into the source per-widget is fragile.

**Use `WslPaths` instead of constructing the UNC string by hand.**

```csharp
using WigiLlm.Shared;

// Distro from WSL_DISTRO env, fallback "Ubuntu":
string p1 = WslPaths.ToWindowsPath("/dev/shm/foo.json");
// → \\wsl.localhost\Ubuntu\dev\shm\foo.json

// Override distro per call:
string p2 = WslPaths.ToWindowsPath("/etc/hosts", "Debian");

// Resolve a path under user home (resolution order: WSL_USER_HOME env →
// WSL_USER env → lowercased Windows username → "/home/user"):
string p3 = WslPaths.UnderHome(".claude/projects");
// → \\wsl.localhost\Ubuntu\home\<user>\.claude\projects
```

| Env var | Default | What it sets |
|---|---|---|
| `WSL_DISTRO` | `Ubuntu` | Distro name |
| `WSL_USER_HOME` | _(see fallback)_ | Full WSL home path, e.g. `/home/foo` |
| `WSL_USER` | _(see fallback)_ | Username only — builds `/home/<name>` if `WSL_USER_HOME` unset |

**Resolution-order fallback for `UserHome`**: if neither env var is set, `WslPaths` lowercases `Environment.UserName` and prepends `/home/` — works when WSL Linux user matches Windows user (the common case). Hard fallback is `/home/user`.

**C# 5 quirk**: `const string FOO = WslPaths.ToWindowsPath(...)` does NOT compile (`const` requires a compile-time constant; method calls aren't allowed). Use `static readonly string FOO = WslPaths.ToWindowsPath(...)` instead. Behaviorally equivalent, computed once at type init.

---

## Importing 3rd-party widgets — what to expect

Several widgets in this repo (ContextMonitor, ControlPanel, MCPHealth, IntegrationHealth, RESTBridge, LLMStatus, MCPNeuralPulse) were imported from outside this codebase — older personal scratch dirs, parallel ClaudeCodeWidgets project. **Treat every imported widget as suspect until you've actually built it.** Things found this session:

### Structurally broken on import

`ContextMonitorWidget` had **two `ContextMonitorWidgetServer` classes and two `ContextMonitorWidgetInstance` classes** living in different namespaces (`ClaudeCodeWidgets` vs `ClaudeCodeWidgets.ContextMonitor`). Neither half had a complete `IWidgetObject` impl with the metadata properties the framework reads. Would have built but never loaded in WigiDash. Fix: pick one namespace, delete the duplicates, ensure exactly one type implements `IWidgetObject` with all required properties.

### csproj `<Compile Include="">` paths break on flatten

ClaudeCodeWidgets project laid out as:
```
ClaudeCodeWidgets.csproj
ContextMonitor/WidgetBase.cs
ContextMonitor/WidgetInstance.cs
```
csproj entries were `<Compile Include="ContextMonitor\WidgetBase.cs" />`. Flattening to per-widget dirs (wigi-llm convention) breaks the path. Fix: rewrite each `Include=` to drop the prefix — `<Compile Include="WidgetBase.cs" />`.

### Orphan WPF references

`LLMStatusWidget.csproj` referenced `SettingsUserControl.xaml` as a `<Page>` but only `.xaml.cs` was imported (no `.xaml`). `InitializeComponent()` was unresolvable. Two options: author the missing XAML, or delete the orphan and make `GetSettingsControl()` return null (matches the launcher widgets — most widgets don't need settings).

### TF / LangVer drift

Imported widgets variously target `v4.7.2` or `v4.8`, `LangVersion 5` or unset. **Standardize to `v4.7.2` / `LangVersion 5`** to match the rest of the repo. Unset LangVer compiles against whatever the build env defaults to today, and breaks subtly when that default shifts.

### Hardcoded user paths everywhere

`/home/<original-author>/...`, `C:\Users\<author>\...`, personal domains as default values, hardcoded WSL distro names. `WslPaths` covers most of these now. For any path you can't centralize, document it as "configure for your environment" and put it behind a settings property or env var.

### Build before believing

Every imported widget was claimed to "work" by its original author. ContextMonitor would have crashed on first load. LLMStatus would have failed at first compile. Assume nothing — stage to `%TEMP%`, run `cmd.exe /c build.bat`, read the actual exit code and warnings.

---

## Pre-publish hygiene — what to scrub before pushing public

The repo is public (https://github.com/Platano78/wigi-llm). Before pushing widgets that came from personal workspaces:

| Surface | Common leak | Fix |
|---|---|---|
| `Author` property in `WidgetBase.cs` | Author's personal name | Set to project name (`"WigiDash"`) |
| `Website` property | Forked-from URL | Point to canonical repo |
| `deploy.bat` | Hardcoded `C:\Users\<author>\...`, `bin\Debug` | `%APPDATA%\G.SKILL\WigiDashManager\Widgets\<GUID>` + `bin\Release` (BrainMonitor pattern) |
| Source `/home/<author>/...` | Hardcoded paths | `WslPaths.UnderHome(rel)` |
| Hardcoded WSL distro `Ubuntu` | Won't work on `Debian` etc | `WslPaths.ToWindowsPath(...)` (uses `WSL_DISTRO`) |
| Default values for `_publicUrl`, `_apiKey`, etc | Personal domain names baked as defaults | Use neutral placeholder (`"--"`) or empty string |
| `Background.png` etc | `C:\Users\<author>\Desktop\...` | `%USERPROFILE%\Pictures\` or bundle the asset in the widget dir |
| AssemblyInfo.cs `[assembly: AssemblyCompany]` | Personal name | Project / org name |

Quick scan command before push:

```bash
git ls-files | xargs grep -l -i "<author-name>\|c:\\\\users\\\\\|/home/<author>" 2>/dev/null
```

---

## Two-agent parallelism (pi + general-purpose)

When you have substantial code-gen + cross-cutting hygiene work that doesn't share files, run them in parallel:

- **fork-pi against `general-qwen36-35b`** for new file generation (200+ LoC, dual-thread orchestration, etc). Spec it with a heredoc so quoting doesn't trip the shell. Always include the strip-down flags (`--no-extensions --no-skills --no-context-files --offline`). Always run the orphan reaper first.
- **General-purpose subagent** for normalization passes (TF/LangVer cleanup, C# 6→5 conversion, build verification across N widgets). Give it explicit "DO NOT TOUCH" widget lists for files pi is writing.

This session did exactly this: pi built MCPPulseWidget (1 widget, 2098 LoC, 4 commits) while general-purpose normalized 4 separate widgets (5 commits including a shared helper). Zero merge conflicts because the touched-file sets were disjoint.

**Spec each agent with stop conditions and verification requirements.** Don't trust any agent's "build verified" claim without seeing the actual msbuild exit code yourself.

---

## fork-pi reliability — what we learned

Pi against `general-qwen36-35b` shipped two widgets this session, ~3380 LoC total. Patterns observed:

### Pi will hallucinate "build verified"

On the first run (NeuralNexus), pi produced a written summary claiming `"Build verified: msbuild Release → 24KB DLL, exit code 0"` despite never having msbuild access in WSL. The DLL didn't exist. On the second run (MCPPulse), the spec explicitly said `"VERIFY exit code 0 and DLL output. If the build fails, fix the errors and rebuild. Do NOT claim 'build verified' without actually running msbuild and seeing exit code 0"` — and pi did real msbuild, real deploy, real numbers that all matched verification.

**Rule**: include explicit verification-with-evidence language in every fork-pi spec, AND verify yourself afterward.

### Pi can produce build-clean C# 5 code on first shot

Both widgets compiled clean with zero errors and zero warnings on first build, despite C# 5's tight constraints. Spec must enumerate the forbidden constructs (no string interpolation, no expression-bodied members, no `out var`, no auto-property initializers, no `volatile double`, etc.) — pi respects the list when it's explicit.

### Pi follows file-write order from the spec

The MCPPulse spec listed files in order (csproj first, then GraphModel, then OrbitalEngine, then ActivityLog, then WidgetInstance, then Widget.cs). Pi produced them in exactly that order, visible via mtime stamps. Useful for catching mid-run progress: if file 3 of 6 has landed, pi is in the implementation phase, not stuck in discovery.

### Pi's stdout is fully buffered

The bg log file stays at 0 bytes for the entire run; it only flushes when pi exits. Don't read the bg log for progress — read the session JSONL at `~/.pi/agent/sessions/--<repo-path>--/<timestamp>.jsonl` instead. JSONL line count + size give a real-time signal.

### Heredoc spec files for non-trivial prompts

Pi's spec for both widgets was 75-155 lines, contained backticks, code samples, JSON examples. Inline as a single argv would have been mauled by the bash double-eval. Pattern:

```bash
cat > /tmp/pi-spec.txt <<'EOFSPEC'
... full spec, single-quoted heredoc disables shell expansion ...
EOFSPEC
SPEC=$(cat /tmp/pi-spec.txt)
pi --print --provider llama --model general-qwen36-35b \
   --tools read,bash,edit,write \
   --no-extensions --no-skills --no-context-files --offline \
   "$SPEC" > /tmp/pi.log 2>&1
```

Always run the orphan reaper before dispatch. Always check the pi process state after dispatch — both for the discovery-hang and post-completion-linger bugs documented in the fork-pi skill.

---

## Final note

**Always build and deploy and visually verify before declaring a feature done.** Type-checks pass on illegal `volatile double`. Pi reports `was_truncated: false` on truncated output. Pi will hallucinate "build verified" if the spec doesn't pin it down. The actual touchscreen + actual router + actual user-tap is the only ground truth.
