# LLM Deck

Physical hardware control panel for managing local LLM model fleets via llama.cpp router.

## What This Is

A configuration and scripting toolkit that turns a G.SKILL WigiDash (or Elgato StreamDeck) into a one-touch control panel for switching between local LLM models. Buttons load/unload models, poll health endpoints, and show real-time status via custom icons.

## Key Files

- `scripts/router-control.sh` — The core script that interfaces with llama.cpp router API
- `wigidash/examples/` — Example buttons.json configurations
- `wigidash/icons/` — Pixel art icon set for model buttons
- `streamdeck/` — StreamDeck adaptation guide

## Architecture

Panel → WSL exec → router-control.sh → llama.cpp router API (port 8081) → GPU

## Rules

- Keep buttons.json examples valid JSON at all times
- router-control.sh must work standalone without any dependencies beyond curl and bash
- Icons are paired: always provide both `icon_name_active.png` and `icon_name_off.png`
- Do not hardcode user-specific paths in shared examples — use YOUR_USER placeholders
- StreamDeck section is documentation only — no plugin code exists yet
