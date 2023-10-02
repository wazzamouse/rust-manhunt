# Overview
Join the manhunt! A timed event that has your players scouring your landscape for their target. The manhunt begins by selecting the target and allowing willing hunters to join the hunt. Every 90 seconds a UAV will fly over and reveal the target's last position on the map and in game. Tailor your hunt to disallow the target from using various vehicles, giving your hunters an edge in this massive fox and hounds style event.


## Permissions
- `manhunt.admin` - Allows a user to run commands that start/end the manhunt event.

## Chat Commands

- `/manhunt start` - (requires `manhunt.admin` permission) - Begins a new event based on the server configuration if one is not already in progress
- `/manhunt end` - (requires `manhunt.admin` permission) - Ends a currently running event, does not calculate rewards
- `/manhunt join` - Joins the currently running event (configuration may prevent same team/clan members from joining and hunting their own team/clan)

## Console Comands
- `mhunt start` - Begins a new event based on the server configuration if one is not already in progress 
- `mhunt end` - Ends a currently running event, does not calculate rewards

## Localization
Default languages supported: `en`, `es`, `fr`, `de`, `ru`

## Configuration
- `Enabled` - (Options: `true`/`false`) will determine if the chat commands will run (Default: `true`)
- `Event Run Time (Minutes)` - (Default: `15`) Set how long the in minutes the hunt portion of the manhunt will last
- `Player Selection` - (Options: `random`) Determines how the target is selected
-- `random` - Selects a random player on the server
- `Hunted Warmup Time (Seconds)` - (Default: `90`) the amount of time the target gets before the hunt begins to prepare and change position
- `No Duplicates` - (Options: `true`/`false`) tracks the last manhunt and attempts to keep the previous hunted from being selected this time  (Default: `true`)
- `No Friendly Kills` - (Options: `true`/`false`) Prevents team/clan members from killing the target or joining the hunt against their team/clan members (Default: `true`) 
- `No Animals` - (Options: `true`/`false`) determines if animals will attack the target (Default: `true`) 
- `Disable Vehicles` - (Options/Default: `air`, `water`, `car`, `animal`) prevents the target from mounting seats of the vehicle type
-- `air` - All air vehicles that have a seat (eg: not Baloons)
-- `water` - All water vehicles that have a seat
-- `car` - All modular ground vehicles
-- `animal` - All animal ground mounts
- `Prize Amount (Server Rewards)` - (Default: 0) amount of Server Rewards RP that gets awarded to the winner of the event (Requires plugin [https://umod.org/plugins/server-rewards](ServerRewards))

### Example Configuration:

```
{
  "Enabled": true,
  "Event Run Time (Minutes)": 15,
  "Player Selection": "random",
  "Hunted Warmup Time (Seconds)": 5.0,
  "No Duplicates": true,
  "No Friendly Kills": true,
  "No Animals": true,
  "Disable Vehicles": [
    "air",
    "water",
    "car",
    "animal"
  ],
  "Prize Amount (Server Rewards)": 1500
}
```
