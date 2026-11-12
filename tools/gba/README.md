# GBA and PSP

Emulators inside the client. The client never ships or changes ROMs, the user brings their own.

## GBA (`src/IsaClient.Gba`)

`.gba`, `.pokemon`, or Games > Game Boy Advance. In Pokémon FireRed/LeafGreen and Emerald the trainer is replaced by
the user's Terraria character.

| File | What |
|---|---|
| `Libretro.cs`, `GbaMachine.cs` | The libretro core on its own thread: picture, sound, buttons, saves, states |
| `Trainer/*` | Builds the trainer sprites from the Terraria character and patches them in memory |
| `PokemonGame.cs`, `PokemonWindow.cs` | State, settings, the window |

The core is mGBA's x86 libretro build, unmodified (`src/IsaClient.Assets/Gba/mgba_libretro.dll.gz`), MPL 2.0,
source at https://github.com/mgba-emu/mgba.

### Tools

`make_heads.py` bakes one head per hairstyle into `src/IsaClient.Assets/PokemonHeads.bin`:

```
python tools/gba/make_heads.py ../terraria-to-pokemon src/IsaClient.Assets/PokemonHeads.bin
```

`reference.py` builds reference ROMs with the Python version of the sprite code, to compare against the C# one:

```
python tools/gba/reference.py ../terraria-to-pokemon <rom.gba> <out dir> <player.plr>...
```

Both need the `terraria-to-pokemon` project next to this repo.

## PSP (`src/IsaClient.Psp`)

`.psp`, or Games > PlayStation Portable.

- The core is PPSSPP's x86 libretro build (GPL 2.0). It is not in the exe, `PspEmulator` downloads it the first time.
- It runs in a second process (this same exe with `--isaclient-psp-host`). In 32 bits PPSSPP needs memory below 2 GB
  that a running Terraria already uses.
- `PspLink` is the shared memory between both processes: picture buffers, sound and commands.
- Formats: ISO, CSO, CHD, EBOOT.PBP, also inside `.zip` and `.7z`. No RAR or ZSO.
