# Wigi-LLM

A physical control surface for local LLM infrastructure, built on the G.SKILL WigiDash touchscreen panel.

The core of this project is **LLM Launcher** — a `buttons.json` config that turns the WigiDash into a one-touch model switcher for your llama.cpp router. No build step, no .NET, just JSON. It's what I use every day.

The repo also includes a suite of experimental C# widgets (model dashboards, brain monitors, a clipboard agent) that go further into custom UI territory. They work, but they're not as battle-tested as the launcher and I rarely reach for them. Treat them as reference implementations or starting points.

---

## Table of Contents

- [LLM Launcher (the main event)](#llm-launcher-the-main-event)
- [Scripts](#scripts)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Configuration Reference](#configuration-reference)
- [Experimental: C# Widgets](#experimental-c-widgets)
- [Project Structure](#project-structure)
- [License](#license)

---

## LLM Launcher (the main event)

**Type:** C# WigiDash widget driven by a `buttons.json` config.

Architecturally a hybrid: a compiled C# widget DLL handles rendering, polling, and click dispatch; a JSON config file drives what each button does. End users typically only edit the JSON — the DLL stays as-is.

- **One-touch model switching** — tap a button, the model loads
- **Radio group behavior** — loading one model auto-unloads others in the same group, preventing VRAM overflow
- **Health polling** — buttons show active/inactive icons based on real-time `/health` checks
- **Remote monitoring** — poll models running on other machines across your network
- **Kill switch** — dedicated button to unload everything and flush VRAM
- **Router auto-start** — automatically starts llama.cpp router if it's not running

**Source:** `wigidash/src/LLMLauncherWidget/` — ~2,100 lines of C# / XAML. Build with `build.bat`, deploy with `deploy.bat`.

**Config examples:**

| File | Description |
|------|-------------|
| `wigidash/examples/buttons-starter.json` | Minimal: kill switch, router toggle, two model slots |
| `wigidash/examples/buttons-full-fleet.json` | Full fleet: kill, router, remote monitors, multiple model presets |

---

## Scripts

### router-control.sh

Core bash script that interfaces with the llama.cpp router API. This is what the WigiDash buttons execute via `wsl --exec`.

| Command | Description |
|---------|-------------|
| `list` | List all available model presets in the router |
| `load <preset>` | Load a model with progress spinner and VRAM tracking |
| `unload <preset>` | Unload a model and verify VRAM release |
| `switch <preset>` | Unload all loaded models, then load the new one (the main command) |
| `status` | Show loaded models with VRAM estimates |

**Features:**
- VRAM-aware switching — checks free VRAM before loading, auto-unloads if needed
- Auto-starts the router if it isn't running
- Drops page cache before model loads to prevent system stalls
- Color-coded terminal output

```bash
# From WSL
./scripts/router-control.sh switch my-model
./scripts/router-control.sh status
./scripts/router-control.sh list
```

### gpu-vram-server.py

Lightweight Python HTTP server for exposing GPU VRAM on remote machines. Used by the experimental widgets for cross-network VRAM monitoring.

```bash
python3 scripts/gpu-vram-server.py            # default port 8089
python3 scripts/gpu-vram-server.py --port 9090
```

| Route | Response |
|-------|----------|
| `GET /gpu` | `{"vram_used_mb": 15719, "vram_total_mb": 16303}` |
| `GET /health` | `{"status": "ok"}` |

No dependencies beyond Python 3 stdlib and `nvidia-smi` on PATH.

---

## Architecture

```
[ WigiDash Panel ]
        |
        v
[ Windows Host ]
        |
        v  (wsl --exec)
[ WSL2 / Linux ]
        |
        v
[ llama.cpp Router API ]   (localhost:8081)
        |
        v
[ GPU ]

Remote monitoring (optional):
  Widget --HTTP--> REMOTE_IP:8089/gpu       (VRAM via gpu-vram-server.py)
  Widget --HTTP--> REMOTE_IP:PORT/health    (model health status)
```

The WigiDash runs on Windows. Model management scripts live in WSL2. The `wsl --exec` bridge connects the two. For remote machines, widgets query HTTP endpoints directly — no WSL needed for monitoring, only for control actions.

---

## Prerequisites

- **G.SKILL WigiDash** + WigiDash Manager software
- **Windows 10/11** with WSL2 installed
- **NVIDIA GPU** with `nvidia-smi` available
- **llama.cpp** running in router mode — or any OpenAI-compatible local LLM server
- **jq** in WSL (`sudo apt-get install jq`) — required by `router-control.sh`
- **Visual Studio 2022 + .NET Framework 4.7.2** — for building the LLM Launcher widget DLL or any of the experimental C# widgets

---

## Quick Start

1. Install WigiDash Manager
2. Build the LLM Launcher widget:
   ```bash
   cd wigidash/src/LLMLauncherWidget
   build.bat
   deploy.bat
   ```
   The widget uses GUID `B8C9D0E1-F2A3-4567-8901-BCDEF1234567` and deploys to:
   ```
   %APPDATA%\G.SKILL\WigiDashManager\Widgets\B8C9D0E1-F2A3-4567-8901-BCDEF1234567\
   ```
3. Copy `wigidash/examples/buttons-starter.json` to that same folder as `buttons.json`
4. Copy the `wigidash/icons/` directory contents to that folder too
5. Edit `buttons.json` — update every `scriptPath` to point to your WSL path:
   ```json
   "scriptPath": "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh switch model-name"
   ```
6. For remote machine monitoring, set `pollHost` to the remote machine IP:
   ```json
   "pollHost": "REMOTE_IP"
   ```
7. Restart WigiDash Manager to load the widget

---

## Configuration Reference

### buttons.json

```json
{
  "version": 2,
  "layout": {
    "type": "dynamic",
    "maxColumns": 4,
    "buttonSpacing": 8,
    "buttonPadding": 4
  },
  "buttons": [
    {
      "id": "model-a",
      "displayName": "Model A",
      "scriptPath": "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh switch model-a",
      "port": 8081,
      "expectedModel": "model-a",
      "serverType": "LlamaCpp",
      "radioGroup": 1,
      "icons": {
        "active": "icon_model_a_active.png",
        "inactive": "icon_model_a_off.png"
      }
    }
  ]
}
```

| Field | Description |
|-------|-------------|
| `id` | Unique button identifier |
| `displayName` | Label shown on the WigiDash |
| `scriptPath` | Command executed on tap. Use `wsl --exec` to bridge to WSL2 scripts |
| `port` | Port to poll for health checks. Set to `-1` for non-pollable buttons (like kill) |
| `pollHost` | IP for remote health polling. Omit for localhost |
| `expectedModel` | Model name to match against the router's loaded model |
| `serverType` | `"LlamaCpp"` for router API, `"Generic"` for plain HTTP 200 checks, `"Kill"` for kill buttons |
| `radioGroup` | Integer group ID. Buttons in a group visually toggle — only one active at a time. Omit for independent buttons |
| `icons.active` | Icon shown when model is loaded / healthy |
| `icons.inactive` | Icon shown when model is unloaded / offline |

---

## Experimental: C# Widgets

> **Status: experimental.** These widgets compile and run, but I rarely use them in daily practice — the buttons.json launcher above covers most of what I actually need. They live here as reference implementations for anyone who wants to build custom WigiDash widgets in C#, or fork pieces of them. Don't expect the same level of polish or stability as the launcher.

A growing collection of C# widgets sharing common helpers (`GpuInfo.cs`, `WslPaths.cs`). All target .NET Framework 4.7.2 with `LangVersion 5` and reference `WigiDashWidgetFramework.dll`.

**LLM server management:**

| Widget | Purpose |
|--------|---------|
| **LLM Control Center** | Full model dashboard: load/unload/switch buttons, VRAM bar, model dropdown, tokens/sec, port health |
| **LLM Brain Monitor** | Full-screen 4×2 visual: animated waveform, VRAM gauge, KV cache, context window, tokens/sec, loading animations |
| **LLM Router Status** | Compact router health: VRAM bar with green/yellow/red thresholds, switch timing, pending requests, remote GPU support |
| **LLM Model Selector** | Minimal model picker: list with loaded indicators, VRAM display, tap-to-switch |
| **LLM Monitor** | LLM server health monitor — polls `/health` and `/v1/models` on llama.cpp / vLLM / Ollama-style endpoints. Different from Stats (which is tokens/sec) |
| **LLM Stats** | Live tokens/sec readout for the loaded model — small, glanceable |

**MCP & Claude Code integration:**

| Widget | Purpose |
|--------|---------|
| **MCP Pulse** | Live MCP server topology + tool-call activity visualization. Polls the MCP gateway `/health` for server-state nodes, tails `mcp.log` for activity beams. Three render modes via double-tap: full / topology-only / activity-only |
| **Context Monitor** | Claude Code context watcher across all live sessions. Reads per-session statusline files from `/dev/shm/claude_statusline_*.json` for context %, filters sessions inactive for >10 min, sorts by most-recent activity. Top-right shows your Anthropic 5h/7d subscription quota (`/dev/shm/claude_usage_cache.json`) with reset countdown — same data `ccstatusline` reads. Animated procedural pixel mascot whose mood tracks the top session's tier (calm / busy / alarm). Three animation intensities via `CC_ANIMATION_INTENSITY` env var. |
| **Integration Health** | Service health for a stack of HTTP/WSL services with start/stop/restart actions. Configurable PowerShell trigger script |
| **Control Panel** | Quick-action button grid for git / build / test / deploy commands. Configurable shell-out per button |

**Other:**

| Widget | Purpose |
|--------|---------|
| **Clipboard Agent** | Hardware clipboard → LLM pipeline. Detects content type (code/JSON/URL/text), runs Summarize/Refactor/Explain/Fix-Bug actions on any OpenAI-compatible endpoint, writes result back to clipboard. Auto-discovers llama.cpp (8081), LM Studio (1234), vLLM (8000), Ollama (11434), Text Generation WebUI (5000) |

**Shared helpers — `wigidash/src/Shared/`:**

- **`GpuInfo.cs`** — VRAM detection. Local mode shells out to `nvidia-smi.exe`; remote mode HTTP-queries `gpu-vram-server.py`. Thread-safe with a 5s cache.
- **`WslPaths.cs`** — translates WSL POSIX paths to Windows UNC form (`\\wsl.localhost\<distro>\...`). Configurable per-environment via env vars (see below). If env vars are unset and the lowercased Windows username doesn't match an existing WSL home dir, it probes `\\wsl.localhost\<distro>\home\` and picks the first real user directory. Strips embedded quote chars from env var values (works around `setx` quoting).

### Per-environment configuration

The widgets that read files from WSL (Context Monitor, MCP Pulse, Integration Health) need to know your distro and home path. Defaults work if your WSL setup mirrors a typical install. Override via environment variables on the Windows side:

| Env var | Default | What it sets |
|---|---|---|
| `WSL_DISTRO` | `Ubuntu` | Distro name in the UNC path |
| `WSL_USER_HOME` | _(see below)_ | Full WSL home path, e.g. `/home/foo`. Use PowerShell's `[Environment]::SetEnvironmentVariable(...)` instead of `setx` — `setx` embeds literal quote chars into the value (the widget defensively strips them, but cleaner to avoid). |
| `WSL_USER` | _(see below)_ | Username only — builds `/home/<name>` if `WSL_USER_HOME` unset |
| `CC_ANIMATION_INTENSITY` | `adaptive` | Context Monitor only. `subtle` = restrained motion everywhere; `bold` = pronounced motion at all tiers; `adaptive` = quiet at calm, bold at busy/alarm. |

Resolution order for the WSL user-home path: `WSL_USER_HOME` → `WSL_USER` → lowercased `Environment.UserName` (verified to exist) → first real directory under `\\wsl.localhost\<distro>\home\`. This last step means the widget usually Just Works without any env var set, even when your WSL username differs from your Windows username.

Env vars are read from the registry at process start, which means **Explorer.exe needs to have inherited the new value before launching WigiDash Manager**. If Explorer was running when you set the var, log out / log back in (or restart Explorer) so WigiDash picks it up.

For the development gotchas list (build flow, threading rules, .NET 4.7.2 / LangVersion 5 limits), see [`docs/wigidash-development-learnings.md`](docs/wigidash-development-learnings.md).

### Building

Each widget compiles to a GUID-named DLL. From a Developer Command Prompt, WSL, or PowerShell:

```bash
msbuild WidgetName.csproj /t:Build /p:Configuration=Release
```

The compiled DLL goes into:

```
%APPDATA%\G.SKILL\WigiDashManager\Widgets\{WIDGET-GUID}\
```

All C# widgets reference `wigidash/src/Shared/GpuInfo.cs` as a linked file — if you build outside Visual Studio, make sure this path resolves correctly.

---

## Project Structure

```
wigi-llm/
├── README.md
├── CLAUDE.md
├── LICENSE
├── scripts/
│   ├── router-control.sh          # CLI wrapper for llama.cpp router API
│   └── gpu-vram-server.py         # Remote GPU VRAM HTTP server
└── wigidash/
    ├── README.md                   # WigiDash-specific setup guide
    ├── examples/
    │   ├── buttons-starter.json    # Minimal config (kill + router + 2 models)
    │   └── buttons-full-fleet.json # Full fleet config with remote monitors
    ├── icons/                      # Pixel art icons (active/inactive pairs)
    └── src/                          # C# widget source
        ├── LLMLauncherWidget/        # The launcher (buttons.json driven) — the daily-driver
        ├── Shared/
        │   ├── GpuInfo.cs            # GPU VRAM detection (local + remote)
        │   └── WslPaths.cs           # WSL POSIX → Windows UNC translation
        ├── LLMControlCenterWidget/   # [experimental] Full model management dashboard
        ├── LLMBrainMonitorWidget/    # [experimental] Full-screen brain visualization
        ├── LLMRouterStatusWidget/    # [experimental] Compact router health display
        ├── LLMModelSelectorWidget/   # [experimental] Minimal model picker
        ├── LLMStatsWidget/           # [experimental] Tokens/sec readout
        ├── LLMStatusWidget/          # [experimental] LLM server health monitor
        ├── MCPPulseWidget/           # [experimental] MCP topology + activity visualization
        ├── ContextMonitorWidget/     # [experimental] Claude Code token-budget watcher
        ├── IntegrationHealthWidget/  # [experimental] Service health + start/stop actions
        ├── ControlPanelWidget/       # [experimental] Quick-action button grid
        └── ClipboardAgentWidget/     # [experimental] Hardware clipboard + LLM actions
```

---

## License

MIT License. See [LICENSE](LICENSE) for details.

Built by a local LLM enthusiast who got tired of typing model switch commands. Contributions welcome — fork it, improve it, send a PR.
