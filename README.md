<p align="center"><img src=".github/logo.gif" alt="IsaClient"></p>

Source of IsaClient, a Terraria 1.4.5.8 client for Windows.

This repo is a mirror. Development happens in a private repo and this one is updated on every release, so the
history here is one commit per version. Looking for the client itself? Get it at https://client.hacks.win.

Terraria's code is not in this repo. You get it by decompiling your own copy of the game; `isa.py` does that and
applies our changes on top.

## What's in here

| Path | What |
|---|---|
| `src/IsaClient.*/` | The client. One folder per module, folder = namespace. |
| `patches/` | Our changes to the game's own files, one `.patch` per file. |
| `plugins/` | Plugin SDK props, examples and the TerraAngel ports. |
| `tools/refactor/` | Roslyn tool used for refactors and checks. |
| `lib/xna/` | XNA reference assemblies, only to compile. |
| `isa.py` | Sets up the source tree and keeps `patches/` in sync. |

`ARCHITECTURE.md` explains how the code is organised and `MODULES.md` lists every class.

## Build

You need:

- Terraria 1.4.5.8 from Steam (Windows)
- .NET 10 SDK
- Python 3.10+

```
git clone https://github.com/magaflaca/isa
cd isa
python isa.py setup
dotnet build src/Terraria.csproj -c Release
```

`setup` finds Terraria through Steam. If it can't, pass the folder: `python isa.py setup --terraria "D:\Games\Terraria"`.
The first run takes a couple of minutes, it decompiles the game into `.vanilla/`.

The exe ends up in `src/bin/Release/net48/Terraria.exe`. Copy it next to your game's `Terraria.exe` under another
name (`IsaClient.exe` for example) and run it from there. Don't overwrite the original.

### Keys

API keys are blanked in this repo. Everything builds without them, but these features need you to put your own
key in the file:

| Feature | File |
|---|---|
| AI assistant (default key) | `src/IsaClient.Assistant/AiAssistant.cs` |
| GIF search | `src/IsaClient.ChatImages/GifSources.cs` |
| Chat pictures, file transfer | `src/IsaClient.ChatImages/ImgurClient.cs` |
| Shared schematics and characters | `src/IsaClient.Sharing/ArchiveStore.cs` |
| Bug reports | `src/IsaClient.Feedback/ReportSender.cs` |
