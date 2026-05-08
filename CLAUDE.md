# Wigi-LLM

A widget suite for the G.SKILL WigiDash touchscreen panel providing hardware-level control and monitoring for local LLM infrastructure.

## What This Is

Six C# WigiDash widgets that turn the panel into a dedicated control surface for local AI. The daily-driver is the LLM Launcher (config-driven via buttons.json); the rest are experimental — model dashboards, brain monitors, router health, clipboard-to-LLM pipeline.

## Key Files

- `scripts/router-control.sh` — CLI wrapper for llama.cpp router API
- `scripts/gpu-vram-server.py` — Remote GPU VRAM HTTP server
- `wigidash/examples/` — Example buttons.json configurations
- `wigidash/icons/` — Pixel art icon set for model buttons
- `wigidash/src/LLMLauncherWidget/` — The daily-driver widget (reads buttons.json at runtime)
- `wigidash/src/Shared/GpuInfo.cs` — Shared GPU VRAM detection helper used by the experimental widgets
- `wigidash/src/{LLMControlCenter,LLMBrainMonitor,LLMRouterStatus,LLMModelSelector,ClipboardAgent}Widget/` — Experimental C# widgets

## Architecture

Panel → Windows → WSL exec → router-control.sh → llama.cpp router API (port 8081) → GPU
Widgets → nvidia-smi.exe (local VRAM) or gpu-vram-server.py (remote VRAM)

## Rules

- Keep buttons.json examples valid JSON at all times
- router-control.sh must work standalone without dependencies beyond curl, bash, and jq
- Icons are paired: always provide both `icon_name_active.png` and `icon_name_off.png`
- Do not hardcode user-specific paths — use YOUR_USER placeholders in examples
- Do not hardcode GPU VRAM sizes — use GpuInfo.cs for auto-detection
- Do not hardcode personal IPs, usernames, or domains in any shared file
- C# widgets target .NET Framework 4.7.2 and must reference WigiDashWidgetFramework.dll (HintPath = bare filename, drop the DLL next to the .csproj when building)
- The experimental widgets link to Shared/GpuInfo.cs via `<Compile Include="..\Shared\GpuInfo.cs" Link="GpuInfo.cs" />` — LLMLauncherWidget is self-contained and does NOT depend on it
- LLMLauncherWidget reads buttons.json from its widget directory at runtime; the JSON is per-user config, never bundled into the DLL
- LLMLauncherWidget has known hardcoded constants for ROUTER_API_HOST/PORT and LLM_SERVER_PORTS at top of WidgetInstance.cs — fine for now, fix if a user reports needing non-default ports
