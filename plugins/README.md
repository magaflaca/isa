# Plugins

IsaClient loads plugins: .NET libraries that run inside the game and draw their windows with Dear ImGui
(ImGui.NET 1.91.0.1). The plugin API is TerraAngel's, so TerraAngel plugins work too.

## Using plugins

Plugins go in `Documents/My Games/Terraria/IsaClient/Plugins` (`.plugins folder` opens it).

| What | How it loads |
|---|---|
| `*.IsaPlugin.dll`, `*.TAPlugin.dll` built for IsaClient | As it is |
| `*.TAPlugin.dll` built for TerraAngel | Decompiled and compiled again for IsaClient |
| `.cs` files in `Plugins/Sources/<name>/` | Compiled by the client |
| A TerraAngel fork in `Plugins/Forks/<name>/` | Built in place of the TerraAngel pack |

Press Insert or type `.plugins` to open the overlay and turn plugins on in the Plugins tab. New plugins start off.
A plugin is code running in your game, only use ones you trust.

Converting and compiling need the developer tools (Roslyn and a decompiler, about 15 MB). They are downloaded the first
time, or with `.plugins devtools`.

Commands: `.plugins`, `.plugins list`, `.plugins on <name>`, `.plugins off <name>`, `.plugins reload [name]`,
`.plugins folder`, `.plugins forks`, `.plugins console`, `.plugins devtools`, `.pc <line>`.

Errors go to the plugin console and to `Plugins/plugins.log`.

## Making a plugin

You need the .NET SDK and IsaClient's exe to compile against.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="path\to\IsaClient.Plugin.props" />
  <PropertyGroup>
    <AssemblyName>MyPlugin.IsaPlugin</AssemblyName>
  </PropertyGroup>
</Project>
```

`dotnet build` compiles it and copies it to the Plugins folder. With the game running, `.plugins reload MyPlugin` loads
the new build.

If the exe is somewhere else: `dotnet build -p:IsaClientExe="C:\Games\Terraria\IsaClient.exe"`.

```csharp
using ImGuiNET;
using IsaClient.Plugins;
using Terraria;

public class MyPlugin : ClientPlugin
{
    public override string Name => "My Plugin";

    bool _fullLife;

    public override void DrawTab(ImGuiIOPtr io)
    {
        ImGui.Checkbox("Full life", ref _fullLife);
    }

    public override void Update()
    {
        if (_fullLife && !Main.gameMenu)
            Main.LocalPlayer.statLife = Main.LocalPlayer.statLifeMax2;
    }
}
```

`Examples/HelloImGui` shows a tab, a world overlay, a tool, a window and commands.
`Examples/TerraAngelExample` is TerraAngel's own example, unchanged.

### ClientPlugin

| Member | What |
|---|---|
| `Name`, `Author`, `Version`, `Description` | Shown in the Plugins tab |
| `Load()` / `Unload()` | Turned on / off |
| `Update()` | Every game frame |
| `DrawTab(io)` | The plugin's tab in the overlay |
| `DrawOverlay(io)` | Every frame over the game, overlay open or not |
| `OnEnterWorld()` | The player joined a world |
| `AddChatCommand(name, usage, summary, handler)` | A `.name` chat command |
| `Chat(text)`, `ChatError(text)` | A chat line |
| `DataFolder` | `Plugins/Data/<Name>` |

### TerraAngel API

Works in both kinds of plugins.

| API | What |
|---|---|
| `TerraAngel.Plugin.Plugin` | Base class of TerraAngel plugins |
| `ClientLoader.Console` | Plugin console: `WriteLine`, `WriteError`, `AddCommand` |
| `ClientLoader.Chat` | Game chat |
| `ClientLoader.MainRenderer.AddWindow(window)` | An ImGui window |
| `ClientLoader.MainRenderer.BindTexture(texture)` | ImGui texture id for a `Texture2D` |
| `ToolManager.AddTool<T>()` | A tool in an overlay tab |
| `ImGuiUtil`, `Util` | Drawing and coordinate helpers |

Everything in `Terraria.*` is public in IsaClient.

### Things to know

- ImGui calls go in `DrawTab`, `DrawOverlay`, `ClientWindow.Draw` or `Tool.DrawUI`. Change game state in `Update`.
- Pair every `ImGui.Begin` with `ImGui.End`.
- An exception in a plugin is caught and logged. A window that throws is closed.
- In `.TAPlugin` projects `Vector2/3/4` convert between XNA and System.Numerics by themselves. `ref` arguments to ImGui
  still need a `System.Numerics` variable.
- After reloading a plugin that other plugins reference, those keep the old copy until the game restarts.

## TerraAngel

`TerraAngel/` is TerraAngel (MIT, see `LICENSE-TerraAngel.txt`) built as a plugin, `TerraAngel.IsaPlugin.dll`.
`Source/` is its code with small changes marked `// Port:`. `Port/` replaces its edits to the game with Harmony patches.

`TerraAngelCommunity/` adds tools people made in their TerraAngel forks: Player Editor, Item Editor, Buff Browser.

Differences from TerraAngel:

- Its chat window replaces the game's only with "TerraAngel Chat" on.
- Anti-hurt turns on IsaClient's God Mode.
- Net Debugger shows raw packet bytes, it does not decode them.
- No world edit in the pack. IsaClient has its own.

### Porting a TerraAngel plugin

TerraAngel runs on .NET 10 with FNA. IsaClient runs on .NET Framework 4.8 x86 with XNA.

A built TerraAngel plugin is converted when you turn it on. If that fails, the errors are in the Plugins tab and the
C# to fix is in `Plugins/Converted/<name>/`.

From source: make a project like the one above, keep the `.TAPlugin` assembly name and copy the code in.
What may need changes:

- Newer .NET APIs. `MathF`, `Math.Clamp`, `Array.Fill`, `Random.Shared` are provided. Spans and ranges are not.
- TerraAngel's library: reference `TerraAngel.IsaPlugin.dll`.
- `GetString(...)`: add `using static TerraAngel.I18n;`.

### Forks

A fork in `Plugins/Forks/<name>/` is built on top of the pack:

- Files the fork did not change keep the pack's version.
- Changed files are merged three-way. On a conflict the fork's file wins.
- New files are added.
- Changes to game files or to TerraAngel's loader and renderer can't be used. Move them to Harmony patches.

Each build writes `Plugins/Compiled/<name>.fork.txt` with what was merged, added and skipped.
