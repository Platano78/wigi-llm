# LLM Deck: StreamDeck Adaptation Guide

## Concept
The core concept remains identical to the WigiDash implementation: utilizing hardware buttons to trigger local LLM model switching. However, the software wiring and execution methods differ due to the Elgato StreamDeck software architecture.

## Manual Setup (No Plugin)
To configure model switching without a dedicated plugin, utilize the native system execution capabilities.

1. Add a "System: Open" action to a button.
2. Set the application field to: wsl.exe
3. Set the arguments field to: --exec /home/user/scripts/router-control.sh switch model-name
4. Assign custom icons to each button manually via the StreamDeck interface.

Note: Ensure the path to router-control.sh matches your specific WSL environment.

## Status Polling Limitations
The native StreamDeck software does not support polling HTTP endpoints to update button states dynamically. You have three options to handle status indication:

1. Use the streamdeck-plugin-sdk to build a custom plugin that polls the router's /health endpoint.
2. Run an external background script that polls the router and pushes state updates to the buttons via the StreamDeck API.
3. Accept a static visual state, foregoing the automatic green/red status indicators present in the WigiDash setup.

## Multi-Action Setup
To improve user feedback in the absence of active polling, utilize the StreamDeck "Multi Action" feature.

* Create a multi-action that first runs the switch command (via wsl.exe), then triggers a system notification or temporary visual change to indicate the command was sent.
* Utilize StreamDeck folders or pages to group model buttons logically by parameter size, architecture, or quantization.

## Icon Format
StreamDeck natively expects icons in 72x72 or 144x144 PNG format. If you are migrating from the WigiDash setup, you may need to resize the provided icons to fit these dimensions optimally.

## Plugin Opportunity
Currently, there is no dedicated StreamDeck plugin for local LLM fleet management. A purpose-built plugin could poll the llama.cpp /v1/models endpoint, display the currently loaded model status dynamically, and trigger switch commands directly.

Contributions for a native plugin are welcome. Refer to the architecture section in the main repository README for API details and integration points.

## Alternatives
The router-control.sh backend is hardware-agnostic. Alternative macro solutions such as TouchPortal, Deckboard (Android), or any programmable QMK/VIA macro pad capable of executing shell commands will work using the same WSL execution method.