# RevivalMod for SPT-AKI 🚑

## Overview
RevivalMod adds a second-chance mechanic to Single Player Tarkov. Instead of immediately dying when taking lethal damage, you'll enter a critical state and can use a defibrillator to revive yourself and continue your raid.

## Features
- **Critical State System**: Enter a weakened state instead of dying immediately
- **Defibrillator Revival**: Press F5 while in critical state to revive using your defibrillator
- **Post-Revival Protection**: Temporary invulnerability period after revival to get to safety
- **AI Behavior Modification**: Bots ignore players in critical state
- **Multiplayer Compatible**: Works with Fika Co-op mod
- **Visual Indicators**: Clear notifications and visual effects for critical state and revival
- **Balance Features**: Movement limitations during critical state and cooldown between revivals

## Requirements
- SPT-AKI (Latest version)
- BepInEx

## Installation
1. Download the latest release ZIP from the Releases section
2. Extract the contents to your SPT-AKI installation directory
3. The mod will be automatically loaded when you start the game

## How To Use
1. **Preparation**: Make sure you have a defibrillator (ID: 60540bddd93c884912009818) in your inventory
2. **Critical State**: When you would normally die, you'll enter critical state instead
3. **Revival**: Press F5 to use your defibrillator while in critical state
4. **Recovery**: After revival, you'll have temporary invulnerability but reduced movement speed
5. **Cooldown**: There's a 3-minute cooldown between uses of the revival system

## Configuration
You can modify settings using BepInEx's built-in configuration system. After running the mod once, settings can be found in:
`BepInEx/config/com.kobethuy.BringMeToLifeMod.cfg`

Key configurable settings include:
- **Revival Item ID**: Which item triggers revival (default: Defibrillator "5c052e6986f7746b207bc3c9")
- **Self Revival Key**: Key to trigger self-revival (default: F)
- **Give Up Key**: Key to die immediately in critical state (default: Backspace)
- **Critical State Duration**: How long you can be in critical state (default: 180 seconds)
- **Revival Cooldown**: Time between revivals (default: 180 seconds)
- **Downed Movement Speed**: Movement speed percentage when downed (default: 50%)
- **Restore Destroyed Body Parts**: Automatically restore destroyed limbs after revival
- **Animation Durations**: Self-revive and teammate revive animation times
- **Testing Mode**: Enables debug keybinds and bypasses item requirements

## Multiplayer Support
When using the Fika Co-op mod, RevivalMod will synchronize player states:
- All players will see when someone enters critical state
- Revival status is shared between players
- Defibrillator item requirements are checked server-side

## Known Issues
- Visual effects may occasionally flicker or not display properly
- Some interactions between revival state and certain game mechanics may cause unexpected behavior

## Troubleshooting
- **Revival not working**: Ensure you have a defibrillator in your inventory
- **Still dying instantly**: Check logs for errors and make sure the mod is properly installed
- **Performance issues**: The mod has minimal performance impact, but disable if experiencing problems

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