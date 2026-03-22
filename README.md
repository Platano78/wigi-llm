# Wigi-LLM

A widget suite for the G.SKILL WigiDash touchscreen panel that provides hardware-level control and monitoring for local LLM infrastructure.

What started as a simple model launcher has grown into seven widgets covering everything from one-touch model switching to a hardware clipboard summarizer. If you run local models and you're tired of typing terminal commands to juggle VRAM, this gives you a physical control surface for all of it.

---

## Table of Contents

- [Widgets](#widgets)
  - [LLM Launcher](#1-llm-launcher)
  - [LLM Control Center](#2-llm-control-center)
  - [LLM Brain Monitor](#3-llm-brain-monitor)
  - [LLM Router Status](#4-llm-router-status)
  - [LLM Model Selector](#5-llm-model-selector)
  - [Clipboard Agent](#6-clipboard-agent)
  - [Shared GpuInfo](#7-shared--gpuinfocs)
- [Scripts](#scripts)
- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Building C# Widgets](#building-c-widgets)
- [Configuration Reference](#configuration-reference)
- [Project Structure](#project-structure)
- [License](#license)

---

## Widgets

### 1. LLM Launcher

**Type:** `buttons.json` widget (no C# code needed)

The original widget. Drop a JSON config file into WigiDash Manager and you get a grid of touchscreen buttons that control your llama.cpp router.

- **One-touch model switching** -- tap a button, the model loads
- **Radio group behavior** -- loading one model automatically unloads others in the same group, preventing VRAM overflow
- **Health polling** -- buttons show active/inactive icons based on real-time `/health` endpoint checks
- **Remote monitoring** -- poll models running on other machines across your network
- **Kill switch** -- dedicated button to unload everything and flush VRAM
- **Router auto-start** -- automatically starts llama.cpp router if it is not running

Two example configs are provided:

| File | Description |
|------|-------------|
| `wigidash/examples/buttons-starter.json` | Minimal setup: kill switch, router toggle, two model slots |
| `wigidash/examples/buttons-full-fleet.json` | Full fleet: kill, router, remote monitors, and multiple model presets |

### 2. LLM Control Center

**Type:** C# WigiDash widget

Full model management dashboard on a single panel tile.

- Load, unload, and switch models with touch controls
- Real-time VRAM bar gauge (auto-detected via `nvidia-smi`, no hardcoded values)
- Active model display with dropdown selector
- Router health status and tokens/sec metrics
- Port health monitoring for multiple servers
- **Touch interactions:** tap buttons to load/unload, tap the model area to cycle through models, long-press for the dropdown selector

### 3. LLM Brain Monitor

**Type:** C# WigiDash widget

Full-screen visual brain monitor designed for the WigiDash 4x2 (fullscreen) layout.

- Animated waveform visualization
- Real VRAM usage gauge from `nvidia-smi`
- Model name and server status display
- KV cache usage, context window size, tokens/sec
- Loading progress animation with visual feedback

### 4. LLM Router Status

**Type:** C# WigiDash widget

Compact router health dashboard for keeping an eye on things at a glance.

- Real VRAM bar with color thresholds (green / yellow / red)
- Model load state and switch timing
- Pending request count
- **Remote GPU monitoring** -- auto-detects whether the router URL points to a remote machine and queries `gpu-vram-server.py` for VRAM data over HTTP

### 5. LLM Model Selector

**Type:** C# WigiDash widget

A simpler model picker for users who want a minimal switching interface.

- Model list with loaded/unloaded indicators
- VRAM usage display
- Straightforward tap-to-switch interaction

### 6. Clipboard Agent

**Type:** C# WigiDash widget

The killer feature -- a hardware "Summarize" button.

Monitors the system clipboard, detects the content type (code, plain text, URL, JSON, etc.), and provides two categories of actions:

**LLM Actions** (require a running local LLM):
- Summarize
- Refactor
- Explain
- Fix Bug

**Local Actions** (no LLM needed):
- Format
- Transform
- Snippet
- Escape

**How it works:**
1. Copy something to your clipboard
2. Tap an action button on the WigiDash
3. The widget sends the clipboard content to your local LLM
4. The result goes back onto your clipboard, ready to paste

**Endpoint auto-discovery** -- scans common local LLM ports on startup:

| Server | Default Port |
|--------|-------------|
| llama.cpp | 8081 |
| LM Studio | 1234 |
| vLLM | 8000 |
| Ollama | 11434 |
| Text Generation WebUI | 5000 |

Works with any OpenAI-compatible API endpoint. If your server speaks `/v1/chat/completions`, it works.

### 7. Shared / GpuInfo.cs

**Type:** Shared C# helper (all C# widgets link to this)

Centralized GPU VRAM detection used by every C# widget in the suite.

- **Local detection:** Calls `nvidia-smi.exe` on Windows to get real-time VRAM usage
- **Remote detection:** Queries a remote `gpu-vram-server.py` endpoint over HTTP, expecting `{"vram_used_mb": N, "vram_total_mb": N}`
- **Thread-safe** with a 5-second cache interval to avoid hammering `nvidia-smi`
- **Optional per-model VRAM estimates** via a JSON config file (format: `{"model-name": "~13GB", ...}`)
- Returns a `VramInfo` object with `UsedMB`, `TotalMB`, `UsedGB`, `TotalGB`, `Percent`, and formatted display strings

---

## Scripts

### gpu-vram-server.py

Lightweight Python HTTP server for exposing GPU VRAM on remote machines. Run it on any machine with an NVIDIA GPU, then point your widgets at it.

```bash
# Default port 8089
python3 scripts/gpu-vram-server.py

# Custom port
python3 scripts/gpu-vram-server.py --port 9090
```

**Endpoints:**

| Route | Response |
|-------|----------|
| `GET /gpu` | `{"vram_used_mb": 15719, "vram_total_mb": 16303}` |
| `GET /health` | `{"status": "ok"}` |

No dependencies beyond Python 3 standard library and `nvidia-smi` on the PATH.

### router-control.sh

Core bash script that interfaces with the llama.cpp router API. This is what the WigiDash buttons actually execute via `wsl --exec`.

**Commands:**

| Command | Description |
|---------|-------------|
| `list` | List all available model presets in the router |
| `load <preset>` | Load a model with visual progress spinner and VRAM tracking |
| `unload <preset>` | Unload a model and verify it released VRAM |
| `switch <preset>` | Unload all loaded models, then load the new one (the main command) |
| `status` | Show loaded models with VRAM estimates |

**Features:**
- VRAM-aware switching -- checks free VRAM before loading, auto-unloads if needed
- Auto-starts the router if it is not running
- Visual loading progress with spinner and live VRAM readout
- Drops page cache before model loads to prevent system stalls
- Configurable load time estimates per model
- Color-coded terminal output

```bash
# From WSL
./scripts/router-control.sh switch my-model
./scripts/router-control.sh status
./scripts/router-control.sh list
```

---

## Architecture

```
[ WigiDash Panel ]
        |
        v
[ Windows Host ] ---------- widgets call nvidia-smi.exe for local VRAM
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

The WigiDash runs on Windows. Model management scripts live in WSL2. The `wsl --exec` bridge connects the two. For remote machines, widgets query HTTP endpoints directly -- no WSL needed for monitoring, only for control actions.

---

## Prerequisites

- **G.SKILL WigiDash** + WigiDash Manager software
- **Windows 10/11** with WSL2 installed
- **NVIDIA GPU** with `nvidia-smi` available (the widgets auto-detect VRAM through it)
- **llama.cpp** running in router mode -- or any OpenAI-compatible local LLM server (the Clipboard Agent works with anything that speaks `/v1/chat/completions`)
- **Visual Studio 2022** (for building C# widgets)
- **.NET Framework 4.8**
- **jq** (required by `router-control.sh` -- install with `sudo apt-get install jq` in WSL)

---

## Quick Start

### LLM Launcher (buttons.json -- no build needed)

1. Install the WigiDash Manager software
2. Create a custom widget in the manager -- note the GUID it generates
3. Copy `wigidash/examples/buttons-starter.json` to:
   ```
   %APPDATA%\G.SKILL\WigiDashManager\Widgets\{YOUR-GUID}\buttons.json
   ```
4. Copy the `wigidash/icons/` directory contents into the same widget folder
5. Edit `buttons.json` -- update every `scriptPath` to point to your WSL path:
   ```json
   "scriptPath": "wsl --exec /home/YOUR_USER/wigi-llm/scripts/router-control.sh switch model-name"
   ```
6. For remote machine monitoring, set `pollHost` to the remote machine IP:
   ```json
   "pollHost": "REMOTE_IP"
   ```
7. Restart WigiDash Manager to load the new configuration

### C# Widgets

1. Clone the repo
2. Open the desired widget `.csproj` in Visual Studio 2022
3. Build in Release mode (see [Building C# Widgets](#building-c-widgets) below)
4. Copy the output DLL to the WigiDash widget directory
5. Restart WigiDash Manager

---

## Building C# Widgets

Each C# widget compiles to a GUID-named DLL. From a Developer Command Prompt, WSL, or PowerShell:

```bash
msbuild WidgetName.csproj /t:Build /p:Configuration=Release
```

The compiled DLL goes into:

```
%APPDATA%\G.SKILL\WigiDashManager\Widgets\{WIDGET-GUID}\
```

All C# widgets reference `wigidash/src/Shared/GpuInfo.cs` as a linked file. If you are building outside Visual Studio, make sure this path resolves correctly.

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

**Key fields:**

| Field | Description |
|-------|-------------|
| `id` | Unique button identifier |
| `displayName` | Label shown on the WigiDash |
| `scriptPath` | Command executed on tap. Use `wsl --exec` to bridge to WSL2 scripts |
| `port` | Port to poll for health checks. Set to `-1` for non-pollable buttons (like kill) |
| `pollHost` | IP address for remote health polling. Omit for localhost |
| `expectedModel` | Model name to match against the router's loaded model |
| `serverType` | `"LlamaCpp"` for router API, `"Generic"` for plain HTTP 200 health checks, `"Kill"` for kill buttons |
| `radioGroup` | Integer group ID. Buttons sharing a group visually toggle -- only one shows active at a time. Omit for independent buttons |
| `icons.active` | Icon shown when the model is loaded / healthy |
| `icons.inactive` | Icon shown when the model is unloaded / offline |

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
    └── src/
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

Built by a local LLM enthusiast who got tired of typing model switch commands. Contributions welcome -- fork it, improve it, send a PR.
