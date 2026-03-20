# KeepMeAlive for SPT-AKI

## Overview
KeepMeAlive adds a second-chance mechanic to SPT. Instead of immediately dying to lethal damage, your PMC can enter a critical (downed) state and recover through the revive system.

## Features
- Critical/downed state instead of immediate death in supported cases.
- Self revive flow with configurable keybind, hold time, and revive item requirement.
- Teammate revive flow for co-op sessions.
- Configurable post-revive effects (health restore percentages, invulnerability window, optional status effects).
- Movement limits while downed, plus optional hardcore/death-mode toggles.
- Optional debug switches for testing and diagnosis.

## Requirements
- SPT-AKI
- Fika (required by this plugin build)

## Installation
1. Download the latest release files.
2. Extract them into your SPT root folder
3. Start the game once to generate config files.

## Quick Start
1. Carry your configured revive item (default template ID: `5c052e6986f7746b207bc3c9`).
2. Enter raid normally.
3. If you enter critical state, hold the self-revive key (default: `F`) or wait for teammate to revive you.
4. Use give-up key (default: `Backspace`) if you want to forfeit immediately.

## Configuration
After first launch, configure values in:
`BepInEx/config/com.KeepMeAlive.cfg`

Common settings:
- `Self Revival Key` (default `F`)
- `Give Up Key` (default `Backspace`)
- `Revival Item ID`
- `Critical State Duration` (default `180` seconds)
- `Self Revive Hold Time` and `Team Revive Hold Time`
- `Self Revive Progress Duration` and `Teammate Revive Progress Duration`
- `Self: Revival Cooldown` (default `240` seconds)
- `Downed Movement Speed` (default `50`)
- `Consume Revive Item on Self Revive` / `Consume Revive Item on Teammate Revive`
- `Enable Team Revive`, `No Revive Item Required`, `Debug` toggles


## Troubleshooting
- Revive does not trigger: verify revive item ID and keybind in the config.
- Dying instantly: check whether hardcore/death settings are enabled.
- Unexpected behavior in co-op: ensure all players are on matching mod/plugin versions.

## Credits
- Developed by KaiKiNoodles
- Special thanks to the SPT-AKI development team
- Fika Co-op integration support from the Fika team

## License
This project is licensed under the MIT License - see the LICENSE file for details.

## Support
If you encounter any issues or have suggestions for improvement, please open an issue on the GitHub repository or contact me through the SPT-AKI Discord.

---

*Note: This mod is not affiliated with or endorsed by Battlestate Games. Use at your own risk in accordance with the SPT-AKI project guidelines.*