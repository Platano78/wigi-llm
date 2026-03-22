# WigiDash Integration for LLM Deck

## Overview
The G.SKILL WigiDash is a 7-inch touchscreen PC command panel powered via USB. This guide details how to configure the WigiDash to interface with LLM Deck for controlling local language models.

## Prerequisites
Ensure the following components are installed and configured:
* WigiDash Manager installed on the Windows host.
* llama.cpp configured and running in router mode.
* Windows Subsystem for Linux 2 (WSL2) installed and operational.

## Creating a Custom Widget
To initialize the integration, you must create a dedicated widget within the WigiDash software:
1. Open the WigiDash Manager application.
2. Create a new widget and assign it a unique GUID (e.g., B8C9D0E1-F2A3-4567-8901-BCDEF1234567).
3. All widget configuration files must be placed in the following Windows directory:
   `%APPDATA%\G.SKILL\WigiDashManager\Widgets\{YOUR-GUID}\`

## File Placement
Move the required configuration and asset files into your newly created widget directory:
* `buttons.json`: Place this in the widget root directory.
* `icon_*.png`: Place all image assets in the same root directory.
* Icon naming convention: Files must be named strictly as `icon_{modelname}_active.png` and `icon_{modelname}_off.png` to map correctly to the model states.

## Wiring WSL Commands
WigiDash operates in Windows, while LLM Deck scripts run in WSL2. You must bridge these environments using the `wsl --exec` command.
* In your `buttons.json`, format the `scriptPath` as follows:
  `"scriptPath": "wsl --exec /home/user/scripts/router-control.sh switch model-name"`
* The `wsl --exec` prefix passes the execution directly to the WSL2 Linux environment.
* Verify that all target bash scripts have execution permissions applied within WSL2 (`chmod +x /path/to/script.sh`).

## Radio Groups
To ensure the WigiDash interface accurately reflects the currently loaded model, utilize the radio group functionality:
* Assign the identical `radioGroup` integer value to all model selection buttons in `buttons.json`.
* WigiDash will automatically handle the visual toggle logic: the active model will display its active icon, while all other buttons sharing the same `radioGroup` integer will revert to their inactive icons.
* System-level buttons (e.g., Kill processes, Router ON) operate independently and should NOT have a `radioGroup` assigned.

## Health Polling
To display real-time status indicators, configure the health polling parameters in your JSON configuration:
* `port`: Defines the network port to query for the `/health` endpoint.
* `pollHost`: Defines the IP address for remote machines. Omit this parameter entirely if the service is running on localhost.
* `serverType`: Define the server architecture. Use `"LlamaCpp"` for llama.cpp endpoints, or `"Generic"` for other standard HTTP health endpoints.

## Troubleshooting
* Buttons not appearing: The WigiDash Manager caches configurations. Restart the WigiDash Manager application to force a reload of `buttons.json`.
* Scripts not running: Verify the absolute path to the script within the WSL environment. Ensure the `wsl --exec` prefix is present and the script is executable.
* Icons not showing: Check for typos in the filenames. Ensure they strictly adhere to the `icon_{modelname}_active.png` and `icon_{modelname}_off.png` convention and match the model name defined in the JSON.