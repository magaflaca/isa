using System;
using System.Collections.Generic;
using ImGuiUtil = TerraAngel.Graphics.ImGuiUtil;
using Newtonsoft.Json;
using System.IO;
using TerraAngel.Tools.Inspector;

namespace TerraAngel.Tools.Developer;

//Prueba
// Color guardado como RGB en bytes (sin depender de PackedValue)
public class PresetColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public static PresetColor From(Color c) => new PresetColor { R = c.R, G = c.G, B = c.B };
    public Color ToColor() => new Color((int)R, (int)G, (int)B);
}

// Un preset guarda SOLO la apariencia. No guarda Male: el genero ya va
// incluido en skinVariant (Male se calcula a partir de el).
public class PlayerPreset
{
    public string PresetName { get; set; } = "Preset";
    public int SkinVariant { get; set; }
    public int Hair { get; set; }
    public byte HairDye { get; set; }
    public PresetColor HairColor { get; set; } = new PresetColor();
    public PresetColor EyeColor { get; set; } = new PresetColor();
    public PresetColor SkinColor { get; set; } = new PresetColor();
    public PresetColor ShirtColor { get; set; } = new PresetColor();
    public PresetColor UnderShirtColor { get; set; } = new PresetColor();
    public PresetColor PantsColor { get; set; } = new PresetColor();
    public PresetColor ShoeColor { get; set; } = new PresetColor();
}

public static class PlayerPresetManager
{
    private static string FolderPath => Path.Combine(ClientLoader.SavePath, "PlayerPresets");

    private static string FilePath(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return Path.Combine(FolderPath, name + ".json");
    }

    public static PlayerPreset Capture(Player p, string presetName) => new PlayerPreset
    {
        PresetName = presetName,
        SkinVariant = p.skinVariant,
        Hair = p.hair,
        HairDye = p.hairDye,
        HairColor = PresetColor.From(p.hairColor),
        EyeColor = PresetColor.From(p.eyeColor),
        SkinColor = PresetColor.From(p.skinColor),
        ShirtColor = PresetColor.From(p.shirtColor),
        UnderShirtColor = PresetColor.From(p.underShirtColor),
        PantsColor = PresetColor.From(p.pantsColor),
        ShoeColor = PresetColor.From(p.shoeColor),
    };

    public static void Apply(PlayerPreset s, Player p)
    {
        p.skinVariant = Math.Clamp(s.SkinVariant, 0, PlayerVariantID.Count - 1);
        p.hair = s.Hair;
        p.hairDye = s.HairDye;
        p.hairColor = s.HairColor.ToColor();
        p.eyeColor = s.EyeColor.ToColor();
        p.skinColor = s.SkinColor.ToColor();
        p.shirtColor = s.ShirtColor.ToColor();
        p.underShirtColor = s.UnderShirtColor.ToColor();
        p.pantsColor = s.PantsColor.ToColor();
        p.shoeColor = s.ShoeColor.ToColor();

        // Sincroniza con el servidor para que los demas vean el cambio (en singleplayer no hace nada)
        NetMessage.SendData(4, -1, -1, null, Main.myPlayer);
    }

    public static void Save(PlayerPreset preset)
    {
        Directory.CreateDirectory(FolderPath);
        File.WriteAllText(FilePath(preset.PresetName), JsonConvert.SerializeObject(preset, Formatting.Indented));
    }

    public static void Delete(PlayerPreset preset)
    {
        string path = FilePath(preset.PresetName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public static List<PlayerPreset> LoadAll()
    {
        var list = new List<PlayerPreset>();
        if (!Directory.Exists(FolderPath))
            return list;

        foreach (string file in Directory.GetFiles(FolderPath, "*.json"))
        {
            try
            {
                PlayerPreset? p = JsonConvert.DeserializeObject<PlayerPreset>(File.ReadAllText(file));
                if (p != null)
                    list.Add(p);
            }
            catch
            {
                // json invalido, se ignora
            }
        }
        return list;
    }
}
//Prueba

public class PlayerEditorTool : Tool
{
    public override string Name => GetString("Player Editor");
    public override ToolTabs Tab => ToolTabs.MainTools;

    //Codigo Prueba

    public static string PlayerEditorConfigPath => Path.Combine(ClientLoader.SavePath, "playerEditor.json");

    public static JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
    {
        Formatting = Formatting.Indented,
    };

    public class PlayerEditorConfig
    {
        public int Health = 500;
        public int MaxHealth = 500;
        public int Mana = 200;
        public int MaxMana = 200;
        public float Thorns = 0f;
        public int DashType = 0;
        public int Def = 20;
        public int LifeRegen = 0;
        public int ArmorPenetration = 0;
        public int Aggro = 0;
        public bool NoKnockback = false;
        public bool ShimmerImmune = false;
        public bool LavaImmune = false;
        public bool Ghost = false;
        public bool FunctionalGhost = false;
        public bool ShowBanks = false;
        public bool ShowModerationTools = false;
        public int BuffTime = 240;
    }

    public static void SaveToJson()
    {
        if (Main.LocalPlayer == null)
            return;

        PlayerEditorConfig config = new PlayerEditorConfig
        {
            Health = Main.LocalPlayer.statLife,
            MaxHealth = Main.LocalPlayer.statLifeMax,
            Mana = Main.LocalPlayer.statMana,
            MaxMana = Main.LocalPlayer.statManaMax,
            Thorns = Main.LocalPlayer.ModThorns,
            DashType = Main.LocalPlayer.ModDashType,
            Def = Main.LocalPlayer.ModStatDefense,
            LifeRegen = Main.LocalPlayer.ModLifeRegen,
            ArmorPenetration = Main.LocalPlayer.ModArmorPenetration,
            Aggro = Main.LocalPlayer.ModAggro,
            NoKnockback = Main.LocalPlayer.ModNoKnockback,
            ShimmerImmune = Main.LocalPlayer.ModShimmerImmune,
            LavaImmune = Main.LocalPlayer.ModLavaImmune,
            Ghost = Main.LocalPlayer.ghost,
            FunctionalGhost = Main.LocalPlayer.functionalGhost,
        };

        PlayerInspectorTool inspector = ToolManager.GetTool<PlayerInspectorTool>();
        if (inspector != null)
        {
            config.ShowBanks = inspector.ShowSecondaryInventory;
            config.ShowModerationTools = inspector.ShowModerationTools;
        }

        BuffBrowserTool buffBrowser = ToolManager.GetTool<BuffBrowserTool>();
        if (buffBrowser != null)
        {
            config.BuffTime = buffBrowser.Time;
        }

        string s = JsonConvert.SerializeObject(config, SerializerSettings);
        DirectoryUtility.TryCreateParentDirectory(PlayerEditorConfigPath);
        File.WriteAllText(PlayerEditorConfigPath, s);
    }

    public static void LoadFromJson()
    {
        if (Main.LocalPlayer == null)
            return;

        if (!File.Exists(PlayerEditorConfigPath))
            return;

        string s = File.ReadAllText(PlayerEditorConfigPath);
        PlayerEditorConfig? config = JsonConvert.DeserializeObject<PlayerEditorConfig>(s, SerializerSettings);
        if (config is null)
            return;

        Main.LocalPlayer.statLife = config.Health;
        Main.LocalPlayer.statLifeMax = config.MaxHealth;
        Main.LocalPlayer.statLifeMax2 = config.MaxHealth;
        Main.LocalPlayer.statMana = config.Mana;
        Main.LocalPlayer.statManaMax = config.MaxMana;
        Main.LocalPlayer.statManaMax2 = config.MaxMana;
        Main.LocalPlayer.ModThorns = config.Thorns;
        Main.LocalPlayer.ModDashType = config.DashType;
        Main.LocalPlayer.ModStatDefense = config.Def;
        Main.LocalPlayer.ModLifeRegen = config.LifeRegen;
        Main.LocalPlayer.ModArmorPenetration = config.ArmorPenetration;
        Main.LocalPlayer.ModAggro = config.Aggro;
        Main.LocalPlayer.ModNoKnockback =config.NoKnockback;
        Main.LocalPlayer.ModShimmerImmune = config.ShimmerImmune;
        Main.LocalPlayer.ModLavaImmune = config.LavaImmune;
        Main.LocalPlayer.ghost = config.Ghost;
        Main.LocalPlayer.functionalGhost = config.FunctionalGhost;

        PlayerInspectorTool inspector = ToolManager.GetTool<PlayerInspectorTool>();
        if (inspector != null)
        {
            inspector.ShowSecondaryInventory = config.ShowBanks;
            inspector.ShowModerationTools = config.ShowModerationTools;
        }

        BuffBrowserTool buffBrowser = ToolManager.GetTool<BuffBrowserTool>();
        if(buffBrowser != null)
        {
            buffBrowser.Time = config.BuffTime;
        }
    }

    //Fin Codigo Prueba
    public string statusText = "";

    //Prueba
    private string presetNameBuffer = "";
    private List<PlayerPreset>? presets;
    //Prueba

    public override void DrawUI(ImGuiIOPtr io)
    {

        //Prueba
        // Retrato en vivo del personaje (cuerpo completo). Se actualiza solo cada pocos frames.
        IntPtr livePortrait = TerraAngel.Graphics.PlayerPortraitCache.GetLive();
        if (livePortrait != IntPtr.Zero)
            ImGui.Image(livePortrait, new Vector2(TerraAngel.Graphics.PlayerPortraitCache.Width, TerraAngel.Graphics.PlayerPortraitCache.Height), Vector2.Zero, Vector2.One);
        else
            ImGui.Dummy(new Vector2(TerraAngel.Graphics.PlayerPortraitCache.Width, TerraAngel.Graphics.PlayerPortraitCache.Height));
        ImGui.Text(Main.LocalPlayer.name);
        ImGui.Separator();

        // Galeria de presets de apariencia. Click izquierdo: aplicar. Click derecho: borrar.
        if (ImGui.CollapsingHeader(GetString("Presets"), ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.InputText("##PresetName", ref presetNameBuffer, 32);
            ImGui.SameLine();
            if (ImGui.Button(GetString("Save Preset")))
            {
                string presetName = string.IsNullOrWhiteSpace(presetNameBuffer) ? Main.LocalPlayer.name : presetNameBuffer;
                PlayerPresetManager.Save(PlayerPresetManager.Capture(Main.LocalPlayer, presetName));
                statusText = $"Preset '{presetName}' saved";
                presets = null;
                TerraAngel.Graphics.PlayerPortraitCache.Invalidate();
            }

            presets ??= PlayerPresetManager.LoadAll();

            Vector2 portraitSize = new Vector2(TerraAngel.Graphics.PlayerPortraitCache.Width, TerraAngel.Graphics.PlayerPortraitCache.Height);
            float cellWidth = portraitSize.X + 12f;
            int perRow = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cellWidth));
            int col = 0;
            foreach (PlayerPreset preset in new List<PlayerPreset>(presets))
            {
                // Celda: retrato arriba, nombre abajo
                ImGui.BeginGroup();
                IntPtr portrait = TerraAngel.Graphics.PlayerPortraitCache.Get(preset);
                if (portrait != IntPtr.Zero)
                    ImGui.Image(portrait, portraitSize, Vector2.Zero, Vector2.One);
                else
                    ImGui.Dummy(portraitSize);

                string shownName = preset.PresetName.Length > 14 ? preset.PresetName.Substring(0, 12) + ".." : preset.PresetName;
                ImGui.Text(shownName);
                ImGui.EndGroup();

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text(preset.PresetName);
                    ImGui.TextDisabled(GetString("Left click: apply | Right click: delete"));
                    ImGui.EndTooltip();
                }

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                {
                    PlayerPresetManager.Apply(preset, Main.LocalPlayer);
                    statusText = $"Preset '{preset.PresetName}' applied";
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                {
                    PlayerPresetManager.Delete(preset);
                    statusText = $"Preset '{preset.PresetName}' deleted";
                    presets = null;
                }

                col++;
                if (col < perRow)
                    ImGui.SameLine();
                else
                    col = 0;
            }
            ImGui.NewLine();
        }
        //Prueba

        if (ImGui.BeginTable("PlayerTable", 2))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150.0f);
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Player Name: ");
            ImGui.TableNextColumn();
            ImGui.InputText("##PlayerName", ref Main.LocalPlayer.name, 20);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Health: ");
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerHealth", ref Main.LocalPlayer.statLife);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Max Health: ");
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerMaxHealth", ref Main.LocalPlayer.statLifeMax, Main.LocalPlayer.statLifeMax2);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mana: ");
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerMana", ref Main.LocalPlayer.statMana);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();            
            ImGui.Text("Max Mana: ");
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerMaxMana", ref Main.LocalPlayer.statManaMax, Main.LocalPlayer.statManaMax2);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Life Regem: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerLifeRegen", ref Main.LocalPlayer.ModLifeRegen);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalLifeRegen:0.##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Thorns: "));
            ImGui.TableNextColumn();
            ImGui.InputFloat("##PlayerThorns", ref Main.LocalPlayer.ModThorns);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalThorns:0.##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Armor Penetration: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerAmorPenetration", ref Main.LocalPlayer.ModArmorPenetration);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalArmorPenetration:0.##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Aggro: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerAggro", ref Main.LocalPlayer.ModAggro);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalAggro:0##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Skin Color: "));
            ImGui.TableNextColumn();
            ImGuiUtil.ColorEdit3("##PlayerSkinColor", ref Main.LocalPlayer.skinColor);

            //Prueba
            // Clothing Style = skinVariant. 10 y 11 dan la skin de maniqui.
            // El limite maximo es el mismo que usa el propio juego (PlayerVariantID.Count - 1).
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Clothing Style: "));
            ImGui.TableNextColumn();
            if (ImGui.InputInt("##PlayerSkinVariant", ref Main.LocalPlayer.skinVariant))
            {
                Main.LocalPlayer.skinVariant = Math.Clamp(Main.LocalPlayer.skinVariant, 0, PlayerVariantID.Count - 1);
                NetMessage.SendData(4, -1, -1, null, Main.myPlayer);
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(10-11: mannequin)");
            //Prueba

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Dash Type: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerDashType", ref Main.LocalPlayer.ModDashType);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalDashType:0.##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Defense: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##PlayerDefense", ref Main.LocalPlayer.ModStatDefense);
            ImGui.SameLine();
            ImGui.TextDisabled($"(Base: {Main.LocalPlayer.NaturalStatDefense:0.##})");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox("No Knockback", ref Main.LocalPlayer.ModNoKnockback);
            ImGui.TableNextColumn();
            ImGui.SameLine();
            ImGui.TextDisabled(Main.LocalPlayer.NaturalNoKnockback ? "(Base: Yes)" : "(Base: No)");
                        
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox("Shimmer Immune", ref Main.LocalPlayer.ModShimmerImmune);
            ImGui.TableNextColumn();
            ImGui.SameLine();
            ImGui.TextDisabled(Main.LocalPlayer.NaturalShimmerImmune ? "(Base: Yes)" : "(Base: No)");
                            
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox("Lava Immune", ref Main.LocalPlayer.ModLavaImmune);
            ImGui.TableNextColumn();
            ImGui.SameLine();
            ImGui.TextDisabled(Main.LocalPlayer.NaturalLavaImmune ? "(Base: Yes)" : "(Base: No)");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox("Ghost", ref Main.LocalPlayer.ghost);
            ImGui.TableNextColumn();

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox("Functional Ghost", ref Main.LocalPlayer.functionalGhost);
            ImGui.TableNextColumn();


            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Button(GetString("Save")))
            {
                SaveToJson();
                statusText = "Json Saved";
            }
            ImGui.SameLine();
            if (ImGui.Button(GetString("Load")))
            {
                LoadFromJson();
                statusText = "Json Loaded";
            }

            ImGui.EndTable();

ImGui.Separator();
if (ImGui.Button(GetString("Reset to Base Values")))
{
    Main.LocalPlayer.UseModStatDefense = false;
    Main.LocalPlayer.UseModDashType = false;
    Main.LocalPlayer.UseModThorns = false;
    Main.LocalPlayer.UseModLifeRegen = false;
    Main.LocalPlayer.UseModAggro = false;
    Main.LocalPlayer.UseModArmorPenetration = false;
    Main.LocalPlayer.UseModShimmerImmune = false;
    Main.LocalPlayer.UseModLavaImmune = false;

    // Sincroniza los campos Mod* con el valor natural actual, para que si
    // reactivas el checkbox despues no te salte un numero viejo desactualizado
    Main.LocalPlayer.ModStatDefense = Main.LocalPlayer.NaturalStatDefense;
    Main.LocalPlayer.ModDashType = Main.LocalPlayer.NaturalDashType;
    Main.LocalPlayer.ModThorns = Main.LocalPlayer.NaturalThorns;
    Main.LocalPlayer.ModLifeRegen = Main.LocalPlayer.NaturalLifeRegen;
    Main.LocalPlayer.ModAggro = Main.LocalPlayer.NaturalAggro;
    Main.LocalPlayer.ModArmorPenetration = Main.LocalPlayer.NaturalArmorPenetration;
    Main.LocalPlayer.ModShimmerImmune = Main.LocalPlayer.NaturalShimmerImmune;
    Main.LocalPlayer.ModLavaImmune = Main.LocalPlayer.NaturalLavaImmune;
    statusText = ($"{Main.LocalPlayer.name}" + " Has Been Reseted To Default");
}

            ImGui.Text("Status: " + statusText);

        }
    }
    
}