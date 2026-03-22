# Wigi-LLM

A widget suite for the G.SKILL WigiDash touchscreen panel providing hardware-level control and monitoring for local LLM infrastructure.

## What This Is

Seven widgets (1 buttons.json + 6 C# compiled) that turn a WigiDash into a dedicated control surface for local AI. Covers model switching, VRAM monitoring, router health, and a clipboard-to-LLM pipeline.

## Key Files

- `scripts/router-control.sh` — CLI wrapper for llama.cpp router API
- `scripts/gpu-vram-server.py` — Remote GPU VRAM HTTP server
- `wigidash/examples/` — Example buttons.json configurations
- `wigidash/icons/` — Pixel art icon set for model buttons
- `wigidash/src/Shared/GpuInfo.cs` — Shared GPU VRAM detection helper
- `wigidash/src/*/` — C# widget source for each widget

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
- C# widgets target .NET Framework 4.8 and must reference WigiDashWidgetFramework.dll
- All C# widgets link to Shared/GpuInfo.cs via `<Compile Include="..\Shared\GpuInfo.cs" Link="GpuInfo.cs" />`
