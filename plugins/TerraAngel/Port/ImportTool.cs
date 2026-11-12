namespace TerraAngel.Tools;

/// <summary>Brings in the player's own TerraAngel settings, presets and weapon setups (see TerraAngelImport).</summary>
public class TerraAngelImportTool : Tool
{
    public override string Name => GetString("Import from TerraAngel");

    public override ToolTabs Tab => ToolTabs.MiscTools;

    private string _folder = "";

    public override void DrawUI(ImGuiIOPtr io)
    {
        if (!ImGui.CollapsingHeader(Name))
        {
            return;
        }
        ImGui.TextWrapped(TerraAngelImport.HasTerraAngel ? GetString($"TerraAngel's folder: {TerraAngelImport.TerraAngelFolder}") : GetString("TerraAngel's folder was not found (My Games/Terraria/TerraAngel)."));
        ImGui.BeginDisabled(!TerraAngelImport.HasTerraAngel);
        if (ImGui.Button(GetString("Import TerraAngel's settings")))
        {
            TerraAngelImport.ImportConfig(apply: true);
        }
        ImGui.SameLine();
        ImGuiUtil.HelpMarker(GetString("Replaces the pack's settings with your TerraAngel's (done by itself the first time). Keys that IsaClient uses stay unbound."));
        if (ImGui.Button(GetString("Add presets")))
        {
            int count = TerraAngelImport.ImportPresets();
            ClientLoader.Console.WriteLine(GetString($"{count} preset(s) added."));
        }
        ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.TextUnformatted(GetString("TerraAngel executables (weapon setups):"));
        foreach (string install in TerraAngelImport.Installs)
        {
            ImGui.BulletText(install);
        }
        ImGui.BeginDisabled(TerraAngelImport.Searching);
        if (ImGui.Button(TerraAngelImport.Searching ? GetString("Searching...") : GetString("Search the disk")))
        {
            TerraAngelImport.FindInstalls();
        }
        ImGui.EndDisabled();
        ImGui.SetNextItemWidth(320f);
        ImGui.InputTextWithHint("##TerraAngelFolder", GetString("TerraAngel's folder or .exe"), ref _folder, 512);
        ImGui.SameLine();
        if (ImGui.Button(GetString("Add")))
        {
            TerraAngelImport.AddInstall(_folder);
            _folder = "";
        }
        if (TerraAngelImport.Status.Length > 0)
        {
            ImGui.TextWrapped(TerraAngelImport.Status);
        }
    }
}
