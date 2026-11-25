# Architecture

IsaClient is Terraria 1.4.6.1 rebuilt from its decompiled source with the client's code added on top.
The game stays in its `Terraria.*` namespaces. Everything IsaClient adds lives in `src/IsaClient.*`, folder = namespace.

`MODULES.md` lists every class with one line about what it does.

## Layout

| Namespace | What |
|---|---|
| `IsaClient.Core` | Version, updater, logging, chat output, settings, chat commands, frame lifecycle |
| `IsaClient.Gameplay` | Player cheats, auto clicker, items, buffs, NPCs, combat helpers |
| `IsaClient.Movement` | Noclip, teleports, free camera |
| `IsaClient.Rendering` | Fullbright, zoom, hitboxes, shaders, cosmetics, overlays |
| `IsaClient.WorldTools` | World info, bosses, invasions, map, chests |
| `IsaClient.Building` | World edit: selection, brushes, paste, schematics, pixel art |
| `IsaClient.Networking` | Server detection, packet pacing, rejoin, proxy, server modes, network versions, crossplay |
| `IsaClient.Net.Pipe` | Client to client data channel |
| `IsaClient.Community` | Player list, spectating, chat tools |
| `IsaClient.ChatImages` | Pictures and GIFs in chat |
| `IsaClient.Sharing`, `IsaClient.Transfer` | Characters, styles, schematics and files sent through chat |
| `IsaClient.Items` | Custom items: My Items, item files, item sharing |
| `IsaClient.Identity` | The client's permanent id |
| `IsaClient.Music`, `IsaClient.Video` | Music and video players |
| `IsaClient.Assistant` | AI assistant, `.ask`, Plugin Maker |
| `IsaClient.MultiClient` | Bots and possession |
| `IsaClient.Games`, `IsaClient.Casino`, `IsaClient.Gba`, `IsaClient.Psp` | Mini games and emulators |
| `IsaClient.Menu` | Classic panel, main menu overlay |
| `IsaClient.Hub` | New panel |
| `IsaClient.Plugins` | Plugin system (ImGui, loader, TerraAngel API) |
| `IsaClient.Feedback` | Bug reports, crash reports, polls |
| `IsaClient.Languages` | Translations |
| `IsaClient.Assets` | Embedded pictures and data |
| `IsaClient.Partials` | Members added to game classes |

## Game files

Files under `src/Terraria*` are the game plus thin hooks. A hook reads a module's state or calls it:

```csharp
if (Noclip.Enabled) ...
AutoClicker.Apply(player);
```

The logic goes in the module, not in the game file.

When a game class needs a new member (a field, a property, a helper that touches private state), the class is
`partial` and the member goes in `src/IsaClient.Partials/<game folder>/<file>.cs`, same namespace as the game class.

Two things stay in the game file:

- Game methods that were rewritten or copied with changes (`Player.ItemCheck_Shoot_Inner`, `TileObject.CanPlace`...).
- Additions to `ItemID.Sets` / `BuffID.Sets`. They depend on the `Factory` field and initializer order across partial
  files is not defined. For the same reason, never move a static field whose initializer reads another static of the
  same class into a partial file.

## Game versions

The game is the decompile of Terraria 1.4.6.1 (Steam, `ilspycmd 10.1.1.8388 -p`), kept clean on the branch `vanilla`.

**Joining older servers.** [F5] in the main menu picks the protocol (`NetworkVersion`): 1.4.6.1, 1.4.5.8 or 1.4.5.6.
Each older one translates only what changed after it, in layers:

| Module | Used on | Translates |
|---|---|---|
| `Compat1458` | 1.4.5.8 and 1.4.5.6 | What 1.4.6 changed: packets 41, 88, 91, 117, no 163-165, no trap bomb |
| `Compat1456` | 1.4.5.6 | What 1.4.5.7 changed: projectile keys, NPC slots, item packets, NetModule ids, newer content |

Bots join with the version picked when they were created. `Crossplay` decides if the game may also try UDP (GNS):
only on 1.4.6.1, without proxy, and with `GNS_Wrapper.dll` in the game folder.

**Older game folders.** The exe also runs from a folder older than the client. `GameFolder` skips native libraries that
aren't there, `GameFolderContent` gives a stand-in for textures that aren't there (lowest priority, a complete folder
never reaches it), and OpenNAT is embedded (`lib/opennat`, MIT). `ClientKeys` keeps the client's key bindings in
`IsaKeys.json`, because vanilla rewrites `input profiles.json` without them.

**Updating to a new game version.**

1. Decompile the new `Terraria.exe` with the same ilspycmd and options, and commit it on top of `vanilla`.
2. Merge each changed file three ways: base = the previous `vanilla`, ours = `src/`, theirs = the new decompile
   (`git merge-file`). New files are copied, unchanged ones taken as they are.
3. Many conflicts are only the decompiler numbering locals differently (`num12` becomes `num15`). Resolve them by mapping
   the names, then check our added lines for locals that now mean something else: they still compile.
4. Some game files are stored with LF and the decompile has CRLF: normalize before merging or every line conflicts.
5. Diff the packets (`MessageBuffer.GetData`, `NetMessage.SendData`) between both versions and add what changed to a new
   compat layer under `NetworkVersion`.
6. Update `tools/mirror/config.py` (`GAME`, `EXE_SHA256`) and push `vanilla` with the merge.

## Rules

- One module, one job. A module is a `static class` named after what it does (`Noclip`, `AutoClicker`).
- No `partial` classes outside `IsaClient.Partials`.
- A namespace or member never shares a name with a game type (`Player`, `Audio`, `Mouse`...). It would hide it.
- PascalCase for types, methods and non-private fields. `_camelCase` for private fields. Methods are verbs.
- Toggles are public static fields so the panel can bind them by `ref`.
- Text shown to the user is in English.
- Native functions keep their real name, or set `EntryPoint`.

## Adding things

### Chat command

Handler in the module it belongs to:

```csharp
internal static void MyThingCommand(CommandContext command)
{
    if (command.ArgumentCount < 1) { ClientChat.Error("Usage: .mything <value>"); return; }
    ClientChat.Info("Done: " + command.Parts[1]);
}
```

Register it in `IsaClient.Core/CommandCatalog.cs`:

```csharp
CommandRegistry.Register(MyModule.MyThingCommand, "mything", "mt")
    .Help(CommandSection.World, "<value>", "Does my thing with the value.");
```

The first registration of a name wins. `.help` is built from these entries.

### Saved setting

```csharp
[Setting("MyFeature")] public static bool Enabled;
```

- `Remembered = true` for cheat toggles.
- `Min` / `Max` clamp numbers on load.
- Never change a released key. To rename, add `LegacyKey = "OldKey"`.

### Panel row

A row exists in the classic panel and is then placed in the new one.

Classic panel, in the category class in `IsaClient.Menu` (`PlayerMenu`, `TravelMenu`...):

1. Add the name to `Entries`.
2. Build it in `Create`:
   ```csharp
   case "My Feature":
       return MenuControls.MakeToggle(cheat, color, () => MyModule.Enabled, delegate(UIMouseEvent evt)
       {
           MenuControls.OnToggleClicked(evt, cheat, ref MyModule.Enabled);
       });
   ```
3. Add its hover text in `Tooltip`.

New panel, in `GameplayContent`, `WorldContent` or `ClientContent`:

```csharp
b.Row("My Feature", "Friendly Name", "One line that says what it does.", ItemID.SomeItem, "search words", ".mycmd");
```

A classic row that is not placed shows up under "More". `.menu check` lists them.

### Chat output

`ClientChat.Info`, `ClientChat.Notice`, `ClientChat.Error`. Don't call `Main.NewText` directly for client messages.

### Per-frame work

Through `ClientLifecycle`: `OnUpdate`, `OnEarlyUpdate`, `UpdateInWorld`.

### New module

A `static class` in the right namespace with a `<summary>`, then regenerate the index:

```
python tools/refactor/scripts/module_index.py
```

## Refactor tool

`tools/refactor` is a Roslyn tool that loads the whole project and rewrites every reference. Run it from its folder:
`dotnet bin/Release/net8.0/refactor.dll <command>`.

| Command | What |
|---|---|
| `check` | Compile in memory, print errors |
| `move <plan>` | Move static members to other classes |
| `rename-batch <plan>` | Rename members and types |
| `retarget <plan>` | Change a type's namespace or name |
| `tidy <prefix>` | Drop redundant qualification and unused usings |
| `find-recursion <prefix>` | Members that call themselves |
| `check-init-order <prefix>` | Static initializers that read fields declared later |
| `list-members <prefix>` | Member listing |

After a mechanical change: build, then `find-recursion IsaClient.` and `check-init-order IsaClient.`. The compiler does
not catch a method that now calls itself, a static read before it is set, a renamed `[DllImport]` or a changed string.
