# LLM Deck

LLM Deck is a physical control surface integration for managing local large language models. It turns a G.SKILL WigiDash touchscreen panel (or an Elgato StreamDeck) into a dedicated, one-touch hardware switcher for your local AI infrastructure.

This project was born out of necessity from a real production setup. I run 7+ different local models on a primary workstation with an RTX 5080 16GB, alongside a remote AI-utility machine. Constantly typing terminal commands to unload one model, free up VRAM, and load another became tedious. LLM Deck solves this by providing a tactile, visual interface to manage GPU memory and model routing instantly.

## Features

* One-touch model switching: Tap a physical button to swap the active model.
* Radio group behavior: Ensures mutual exclusion. Loading a new model automatically unloads the previous one in the same group to prevent GPU VRAM overflow.
* Real-time health polling: Buttons visually indicate status. Green means the model is loaded and ready; red means it is offline or unloaded.
* WSL2 support: Native integration for Windows hosts executing Linux binaries (Windows -> WSL exec -> bash).
* Remote machine monitoring: Poll health endpoints across your local network to monitor models running on secondary utility machines.
* Kill switch: Dedicated hardware button to instantly unload all models and flush VRAM.
* Router auto-start: Automatically spins up the llama.cpp router if it is not currently running.
* Custom pixel art icons: Visual identification for different models (e.g., Llama 3, Mistral, Qwen).
* Native llama.cpp router mode support: Built specifically to interface with the llama.cpp router API.

## Architecture

The system bridges physical hardware on Windows to a Linux-based LLM backend via WSL2, while also supporting remote network polling.

```text
[ Physical Panel ]
(WigiDash / StreamDeck)
         |
         v
[ Windows Host ]
(wsl --exec)
         |
         v
[ WSL2 Environment ]
(scripts/router-control.sh)
         |
         v
[ llama.cpp Router API ] <--- (Local GPU)
(localhost:8081)

================================================

[ Remote Monitoring ]
Panel polls network endpoints directly:
-> remote-machine:8083/health
-> remote-machine:8084/health
```

## Hardware Requirements

* Control Panel: G.SKILL WigiDash ($99) or Elgato StreamDeck ($149+)
* GPU: Any GPU capable of running llama.cpp (tested on RTX 5080 16GB)
* OS: Windows 10/11 with WSL2 installed
* Optional: Second machine on the local network for remote model hosting

## Quick Start: WigiDash

The WigiDash is the recommended hardware due to its larger grid and lower price point.

1. Install the official WigiDash Manager software.
2. Create a custom widget in the manager. Note the unique GUID generated for this widget.
3. Copy the `buttons.json` and the `icons/` directory from `wigidash/examples/` into your WigiDash widget configuration folder.
4. Edit `buttons.json` to update the `scriptPath` to point to your local WSL installation path.
5. Restart the WigiDash Manager to load the new configuration and icons.

## Quick Start: StreamDeck

While a dedicated StreamDeck plugin is in development, you can use the native system integration immediately.

1. Open the StreamDeck software.
2. Drag a "System > Open" action onto a button.
3. Set the App/File path to execute the WSL command directly:
   `wsl.exe --exec /home/user/llm-deck/scripts/router-control.sh switch llama3`
4. Assign your custom icons manually in the StreamDeck UI.

## Configuration: buttons.json

For WigiDash setups, the `buttons.json` file dictates the behavior, polling, and visual state of the panel.

```json
{
  "buttons": [
    {
      "id": "btn_llama3",
      "displayName": "Llama 3 8B",
      "scriptPath": "wsl --exec /home/user/llm-deck/scripts/router-control.sh switch llama3",
      "port": 8081,
      "pollHost": "localhost",
      "expectedModel": "llama3-8b-instruct",
      "serverType": "LlamaCpp",
      "radioGroup": "primary_gpu",
      "icons": {
        "active": "icons/llama3_green.png",
        "inactive": "icons/llama3_red.png"
      }
    },
    {
      "id": "btn_remote_whisper",
      "displayName": "Whisper (Remote)",
      "scriptPath": "",
      "port": 8083,
      "pollHost": "192.168.1.150",
      "expectedModel": "whisper-large",
      "serverType": "Generic",
      "radioGroup": "none",
      "icons": {
        "active": "icons/whisper_green.png",
        "inactive": "icons/whisper_red.png"
      }
    }
  ]
}
```

Key fields:
* `radioGroup`: Buttons sharing the same group ID will mutually exclude each other. Pressing one unloads the others.
* `pollHost` / `port`: Used by the panel to check the `/health` endpoint and toggle the active/inactive icon.
* `serverType`: Set to `LlamaCpp` for native router API support, or `Generic` for basic HTTP 200 OK health checks.

## API: router-control.sh

The bash script acts as the translation layer between the physical button press and the llama.cpp router.

* `list`: Outputs all currently configured models in the router.
* `switch <model_name>`: The primary command. Unloads the current model in the radio group and loads the requested model.
* `load <model_name>`: Forces a model to load without checking radio group constraints.
* `unload <model_name>`: Unloads a specific model, freeing VRAM.
* `status`: Returns the current health and VRAM usage of the router.

## Adding New Models

To add a new model to your deck:

1. Add the model configuration to your `router-config.ini` (used by llama.cpp).
2. Create or download active (green) and inactive (red) pixel art PNGs for the `icons/` folder.
3. Add a new entry to `buttons.json`. Ensure the `radioGroup` matches your primary GPU group so it unloads existing models.
4. Restart your panel manager software.

## Project Structure

```text
llm-deck/
├── README.md
├── CLAUDE.md
├── docs/
├── scripts/
│   └── router-control.sh
├── streamdeck/
│   └── README.md
└── wigidash/
    ├── README.md
    ├── examples/
    │   ├── buttons.json
    │   └── router-config.ini
    └── icons/
```

## License

MIT License. 

Built by a local LLM enthusiast who got tired of typing model switch commands. Feel free to fork, modify, and submit pull requests.