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

**Type:** `buttons.json` widget — no C# code, no build step.

Drop a JSON config into WigiDash Manager and you get a grid of touchscreen buttons that drive your llama.cpp router.

- **One-touch model switching** — tap a button, the model loads
- **Radio group behavior** — loading one model auto-unloads others in the same group, preventing VRAM overflow
- **Health polling** — buttons show active/inactive icons based on real-time `/health` checks
- **Remote monitoring** — poll models running on other machines across your network
- **Kill switch** — dedicated button to unload everything and flush VRAM
- **Router auto-start** — automatically starts llama.cpp router if it's not running

Two example configs:

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
- **Visual Studio 2022 + .NET Framework 4.8** — only if you want to build the experimental C# widgets

---

## Quick Start

1. Install WigiDash Manager
2. Create a custom widget in the manager — note the GUID it generates
3. Copy `wigidash/examples/buttons-starter.json` to:
   ```
   %APPDATA%\G.SKILL\WigiDashManager\Widgets\{YOUR-GUID}\buttons.json
   ```
4. Copy the `wigidash/icons/` directory contents into the same widget folder
5. Edit `buttons.json` — update every `scriptPath` to point to your WSL path:
   ```json
   "scriptPath": "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh switch model-name"
   ```
6. For remote machine monitoring, set `pollHost` to the remote machine IP:
   ```json
   "pollHost": "REMOTE_IP"
   ```
7. Restart WigiDash Manager to load the new configuration

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

Five C# widgets sharing a common `GpuInfo.cs` helper for VRAM detection. All target .NET Framework 4.8 and reference `WigiDashWidgetFramework.dll`.

| Widget | Purpose |
|--------|---------|
| **LLM Control Center** | Full model dashboard: load/unload/switch buttons, VRAM bar, model dropdown, tokens/sec, port health |
| **LLM Brain Monitor** | Full-screen 4×2 visual: animated waveform, VRAM gauge, KV cache, context window, tokens/sec, loading animations |
| **LLM Router Status** | Compact router health: VRAM bar with green/yellow/red thresholds, switch timing, pending requests, remote GPU support |
| **LLM Model Selector** | Minimal model picker: list with loaded indicators, VRAM display, tap-to-switch |
| **Clipboard Agent** | Hardware clipboard → LLM pipeline. Detects content type (code/JSON/URL/text), runs Summarize/Refactor/Explain/Fix-Bug actions on any OpenAI-compatible endpoint, writes result back to clipboard. Auto-discovers llama.cpp (8081), LM Studio (1234), vLLM (8000), Ollama (11434), Text Generation WebUI (5000) |

**Shared helper — `wigidash/src/Shared/GpuInfo.cs`:** centralized VRAM detection used by every C# widget. Local mode shells out to `nvidia-smi.exe`; remote mode HTTP-queries `gpu-vram-server.py`. Thread-safe with a 5s cache.

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
    └── src/                        # Experimental C# widgets
        ├── Shared/
        │   └── GpuInfo.cs          # GPU VRAM detection (local + remote)
        ├── LLMControlCenterWidget/ # Full model management dashboard
        ├── LLMBrainMonitorWidget/  # Full-screen brain visualization
        ├── LLMRouterStatusWidget/  # Compact router health display
        ├── LLMModelSelectorWidget/ # Minimal model picker
        └── ClipboardAgentWidget/   # Hardware clipboard + LLM actions
```

---

## License

MIT License. See [LICENSE](LICENSE) for details.

Built by a local LLM enthusiast who got tired of typing model switch commands. Contributions welcome — fork it, improve it, send a PR.
