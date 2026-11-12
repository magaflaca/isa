// TerraAngel's own plugin example (PLUGINS.md), unchanged except for the constructor name the docs got wrong.
// It builds against IsaClient as it is: same namespaces, same Plugin class, same ClientLoader.Console.
using TerraAngel;
using TerraAngel.Plugin;
using Terraria;
using Terraria.DataStructures;

namespace Console.TAPlugin;

public class ConsoleExamplePlugin : Plugin
{
    public override string Name => "Console Example Plugin";

    public ConsoleExamplePlugin(string path) : base(path)
    {

    }

    // Called once on load
    public override void Load()
    {
        // Add a command to the console
        ClientLoader.Console.AddCommand("kill_player",
            (x) =>
            {
                Main.LocalPlayer.KillMe(PlayerDeathReason.ByPlayer(Main.myPlayer), 1, 0);
            },
            "Kills the player");
    }

    // Called once on unload
    public override void Unload()
    {

    }

    // Called every frame that the plugin is loaded
    public override void Update()
    {

    }
}
