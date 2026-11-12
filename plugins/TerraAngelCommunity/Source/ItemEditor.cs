using Newtonsoft.Json;
using System.IO;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace TerraAngel.Tools.Developer;

//Codigo Prueba
public class WeaponConfig
{
    public string ConfigName {get; set;} = "Default";

    public byte TagColorR {get; set;} = 255;
    public byte TagColorG {get; set;} = 255;
    public byte TagColorB {get; set;} = 255;

    public int ItemType {get; set;}
    public string WeaponName {get; set;} = ""; 
    public byte Prefix {get; set;}
    public int Damage {get; set;}
    public int UseTime {get; set;}
    public int AnimationSpeed {get; set;}
    public float Size {get; set;}
    public int Projectile {get; set;}
    public float ProjectileSpeed {get; set;}
    public int TimesToShoot {get; set;} = 1;
    public int PickPower {get; set;}
    public int AxePower {get; set;}
    public int HammerPower {get; set;}
    public int Stack {get; set;}
    public int CritRate {get; set;}
    public float knockBack {get; set;}
    public int UseAmmo {get; set;}
    public int CreateTile {get; set;}
    public int PlaceStyle {get; set;}
    public int CreateWall {get; set;}
    public int Defense {get; set;}
    public int UseStyle {get; set;}
    public int ValueCopper {get; set;}
    public int Buff {get; set;}
    public int BuffTime {get; set;}
    public int HealLife {get; set;}
    public int HealMana {get; set;}
    public int HeadSlot {get; set;}
    public int BodySlot {get; set;}    
    public int LegSlot {get; set;}
    public bool Vanity {get; set;}
    public bool Accessory {get; set;}
    public bool Consumable {get; set;}
    public bool UseTurn {get; set;}
    public bool BlockTurn {get; set;}                
}
public class WeaponConfigFile
{
    public int ItemType {get; set;}
    public string WeaponName {get; set;} = "";
    public List<WeaponConfig> Configs {get; set;} = new();
}

public static class WeaponConfigManager
{
    // Port: IsaClient's folder (TerraAngel kept them next to its executable; TerraAngelImport brings those in).
    private static string BasePath => TerraAngelImport.WeaponConfigsFolder;
    public static Dictionary<int, int> ItemTimesToShoot { get; } = new();
    public static Dictionary<int, string> ConfigName {get;} = new();
    public static Dictionary<int, (byte R, byte G, byte B)> TagColor { get; } = new();
    public static void SaveWeaponConfig(WeaponConfig config)
    {
        Directory.CreateDirectory(BasePath);
        string fullPath = Path.Combine(BasePath, config.ItemType + ".json");

        WeaponConfigFile file;
        if (File.Exists(fullPath))
        {
            file = JsonConvert.DeserializeObject<WeaponConfigFile>(File.ReadAllText(fullPath))
                   ?? new WeaponConfigFile { ItemType = config.ItemType, WeaponName = config.WeaponName };
        }
        else
        {
            file = new WeaponConfigFile { ItemType = config.ItemType, WeaponName = config.WeaponName };
        }

        int existingIndex = file.Configs.FindIndex(c => c.ConfigName == config.ConfigName);
        if (existingIndex >= 0)
            file.Configs[existingIndex] = config;
        else
            file.Configs.Add(config);

        File.WriteAllText(fullPath, JsonConvert.SerializeObject(file, Formatting.Indented));
    }
    public static List<WeaponConfigFile> LoadAllConfigFiles()
    {
        var list = new List<WeaponConfigFile>();
        if (!Directory.Exists(BasePath))
            return list;

        foreach (string file in Directory.GetFiles(BasePath, "*.json"))
        {
            try
            {
                WeaponConfigFile? parsed = JsonConvert.DeserializeObject<WeaponConfigFile>(File.ReadAllText(file));
                if (parsed != null)
                    list.Add(parsed);
            }
            catch
            {
                // json corrupto o invalido, se ignora
            }
        }
        return list;
    }

    public static void DeleteConfig(WeaponConfigFile file, WeaponConfig config)
    {
        string fullPath = Path.Combine(BasePath, file.ItemType + ".json");
        file.Configs.RemoveAll(c => c.ConfigName == config.ConfigName);

        if (file.Configs.Count == 0)
        {
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        else
        {
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(file, Formatting.Indented));
        }
    }
}

//Codigo Prueba

public class ItemEditorTool : Tool
{
    private static Item HeldItem => Main.LocalPlayer.inventory[Main.LocalPlayer.selectedItem];

    public override string Name => GetString("Item Editor");

    public string[]? ItemPrefixes;

    //Codigo Prueba
    private int timesToShootBuffer = 1;
    private int lastHeldItemType = -1;
    private string configNameBuffer = "";
    private Vector3 tagColor = new Vector3(1f, 1f, 1f);
    private List<WeaponConfigFile>? savedFiles;
    private Dictionary<int, Item> previewItemsByType = new();

    private void RefreshSavedConfigs()
    {
        savedFiles = WeaponConfigManager.LoadAllConfigFiles();
        previewItemsByType.Clear();

        foreach (WeaponConfigFile file in savedFiles)
        {
            if (!previewItemsByType.ContainsKey(file.ItemType))
            {
                Item preview = new Item();
                preview.SetDefaults(file.ItemType);
                previewItemsByType[file.ItemType] = preview;
            }
        }
    }
    //Codigo Prueba
    public string statusText = "";
    public override void DrawUI(ImGuiIOPtr io)
    {
        if (HeldItem.stack == 0)
        {
            ImGui.Text(GetString("Hold a item to modify!!!"));
            return;
        }

        //Codigo Prueba
        if (lastHeldItemType != HeldItem.type)
        {
            timesToShootBuffer = WeaponConfigManager.ItemTimesToShoot.GetValueOrDefault(HeldItem.type, 1);
            configNameBuffer = WeaponConfigManager.ConfigName.GetValueOrDefault(HeldItem.type, "Default");

            if (WeaponConfigManager.TagColor.TryGetValue(HeldItem.type, out var storedColor))
            tagColor = new Vector3(storedColor.R / 255f, storedColor.G / 255f, storedColor.B / 255f);
            else
            tagColor = new Vector3(1f, 1f, 1f);

            lastHeldItemType = HeldItem.type;
        }

        //Codigo Prueba

        ImGuiUtil.ItemButton(HeldItem, "InspectorItem", new Vector2(32f));
        ImGui.SameLine();
        ImGui.Text(HeldItem.Name);

        if (ImGui.BeginTable("ItemTable", 2))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150.0f);
            ImGui.TableSetupColumn("Input", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Prefix: "));
            int prefixIndex = HeldItem.prefix;
            ItemPrefixes ??= GetItemPrefixes();
            ImGui.TableNextColumn();
            if (ImGui.Combo("##Prefix", ref prefixIndex, ItemPrefixes, ItemPrefixes.Length))
            {
                HeldItem.prefix = (byte)prefixIndex;
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Damage: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemDamage", ref HeldItem.damage);


            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Usetime: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemUseTime", ref HeldItem.useTime);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Animation Speed: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemAnimationSpeed", ref HeldItem.useAnimation);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Size: "));
            ImGui.TableNextColumn();
            ImGui.InputFloat("##ItemSize", ref HeldItem.scale);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Projectile: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemProjectile", ref HeldItem.shoot);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Projectile Speed: "));
            ImGui.TableNextColumn();
            ImGui.InputFloat("##ItemProjectileSpeed", ref HeldItem.shootSpeed);

            //Codigo Prueba
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Times To Shoot: "));
            ImGui.TableNextColumn();
            if (ImGui.InputInt("##ItemTimesToShoot", ref timesToShootBuffer))
            {
                timesToShootBuffer = Math.Clamp(timesToShootBuffer, 1, 30);
                WeaponConfigManager.ItemTimesToShoot[HeldItem.type] = timesToShootBuffer;
            }
            //Codigo Prueba

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Pick Power: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemPickPower", ref HeldItem.pick);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Axe Power: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemAxePower", ref HeldItem.axe);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Hammer Power: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemHammerPower", ref HeldItem.hammer);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Stack: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemStack", ref HeldItem.stack);

            //Inicio de OpcionesPropias

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Crit Rate: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemCrtiRate", ref HeldItem.crit);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Knockback: "));
            ImGui.TableNextColumn();
            ImGui.InputFloat("##Itemknockback", ref HeldItem.knockBack);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Use Ammo: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemUseAmmo", ref HeldItem.useAmmo);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Create Tile: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##TileID", ref HeldItem.createTile);
            ImGui.TableNextRow();
            ImGui.InputInt("##TileStyle", ref HeldItem.placeStyle);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Create Wall: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemWall", ref HeldItem.createWall);
            
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Defense: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemDefense", ref HeldItem.defense);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Use Style: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemUseStyle", ref HeldItem.useStyle);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Value (copper): "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemValue", ref HeldItem.value);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Buff: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemBuffType", ref HeldItem.buffType);   
            ImGui.TableNextRow();
            ImGui.TableNextColumn();  
            ImGui.Text(GetString("Buff time: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##BuffTime", ref HeldItem.buffTime);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Heal Life: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemHealLife", ref HeldItem.healLife);    
            
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Heal Mana: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemHealMana", ref HeldItem.healMana);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Head Slot: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemHeadSlot", ref HeldItem.headSlot);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Body Slot: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemBodySlot", ref HeldItem.bodySlot);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(GetString("Leg Slot: "));
            ImGui.TableNextColumn();
            ImGui.InputInt("##ItemLegSlot", ref HeldItem.legSlot);
            
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox(GetString("Vanity Item"), ref HeldItem.vanity);
            ImGui.TableNextColumn();                        

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox(GetString("Accesory"), ref HeldItem.accessory);
            ImGui.TableNextColumn();

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox(GetString("Consumable"), ref HeldItem.consumable);
            ImGui.TableNextColumn();            

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox(GetString("Use Turn"), ref HeldItem.useTurn);
            ImGui.TableNextColumn();
        
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Checkbox(GetString("Block Turn"), ref HeldItem.noUseGraphic);
            ImGui.TableNextColumn();

            //final de OpcionesPropias
                               
            ImGui.EndTable();
        }


        ImGui.Checkbox(GetString("Auto-Swing"), ref HeldItem.autoReuse);

        ImGui.Text("Status: " + statusText);                

        if (ImGui.Button(GetString("Restore Item to Default")))
        {
            int stack = HeldItem.stack;
            byte prefix = HeldItem.prefix; 
            HeldItem.SetDefaults(HeldItem.type);
            HeldItem.stack = stack;
            HeldItem.prefix = prefix;

        //Codigo Prueba
            timesToShootBuffer = 1;
            configNameBuffer = "Default";
            tagColor = new Vector3(1f, 1f, 1f);

            WeaponConfigManager.ItemTimesToShoot[HeldItem.type] = 1;
            WeaponConfigManager.ConfigName[HeldItem.type] = "Default";
            WeaponConfigManager.TagColor.Remove(HeldItem.type);
            statusText = ($"{HeldItem.Name}") + " Restored to Default";
        }

        ImGui.Separator();
        ImGui.Text(GetString("Save current stats as a named config:"));
        if (ImGui.InputText("##ConfigName", ref configNameBuffer, 64))
        {
            WeaponConfigManager.ConfigName[HeldItem.type] = configNameBuffer;
        }
        if (ImGui.ColorEdit3(GetString("Tag Color"), ref tagColor))
        {
            WeaponConfigManager.TagColor[HeldItem.type] = 
            (
            (byte)(tagColor.X * 255f),
            (byte)(tagColor.Y * 255f),
            (byte)(tagColor.Z * 255f)
            );
        }

        if (ImGui.Button(GetString("Save Config")))
        {
            WeaponConfig config = new WeaponConfig
            {
                ConfigName = string.IsNullOrWhiteSpace(configNameBuffer) ? "Default" : configNameBuffer,
                TagColorR = (byte)(tagColor.X * 255f),
                TagColorG = (byte)(tagColor.Y * 255f),
                TagColorB = (byte)(tagColor.Z * 255f),
                ItemType = HeldItem.type,
                WeaponName = HeldItem.Name,
                Prefix = HeldItem.prefix,
                Damage = HeldItem.damage,
                UseTime = HeldItem.useTime,
                AnimationSpeed = HeldItem.useAnimation,
                Size = HeldItem.scale,
                Projectile = HeldItem.shoot,
                ProjectileSpeed = HeldItem.shootSpeed,
                TimesToShoot = timesToShootBuffer,
                PickPower = HeldItem.pick,
                AxePower = HeldItem.axe,
                HammerPower = HeldItem.hammer,
                Stack = HeldItem.stack,
                CritRate = HeldItem.crit,
                knockBack = HeldItem.knockBack,
                UseAmmo = HeldItem.useAmmo,
                CreateTile = HeldItem.createTile,
                PlaceStyle = HeldItem.placeStyle,
                CreateWall = HeldItem.createWall,
                Defense = HeldItem.defense,
                UseStyle = HeldItem.useStyle,
                ValueCopper = HeldItem.value,
                Buff = HeldItem.buffType,
                BuffTime = HeldItem.buffTime,
                HealLife = HeldItem.healLife,
                HealMana = HeldItem.healMana,
                HeadSlot = HeldItem.headSlot,
                BodySlot = HeldItem.bodySlot,
                LegSlot = HeldItem.legSlot,
                Vanity = HeldItem.vanity,
                Accessory = HeldItem.accessory,
                Consumable = HeldItem.consumable,
                UseTurn = HeldItem.useTurn,
                BlockTurn = HeldItem.noUseGraphic
            };

            WeaponConfigManager.SaveWeaponConfig(config);
            RefreshSavedConfigs();
            statusText = ($"{config.ConfigName}") + " Saved" ;
        }

        ImGui.Separator();

        if (ImGui.CollapsingHeader(GetString("Saved Weapons")))
        {
            if (ImGui.Button(GetString("Refresh")) || savedFiles == null)
            {
                RefreshSavedConfigs();
                statusText = "Weapons Refreshed";
            }

            int totalConfigs = savedFiles?.Sum(f => f.Configs.Count) ?? 0;
            ImGui.SameLine();
            ImGui.Text($"({totalConfigs})");

            if (savedFiles != null && savedFiles.Count > 0)
            {
                foreach (WeaponConfigFile file in new List<WeaponConfigFile>(savedFiles))
                {
                    if (file.Configs.Count == 0) continue;

                    if (!previewItemsByType.TryGetValue(file.ItemType, out Item? preview))
                    {
                        preview = new Item();
                        preview.SetDefaults(file.ItemType);
                        previewItemsByType[file.ItemType] = preview;
                    }

                    ImGuiUtil.ItemButton(preview, $"WeaponGroup_{file.ItemType}", new Vector2(32f));
                    ImGui.SameLine();
                    ImGui.Text(file.WeaponName);

                    ImGui.Indent();
                    foreach (WeaponConfig config in new List<WeaponConfig>(file.Configs))
                    {
                        Vector4 swatch = new Vector4(config.TagColorR / 255f, config.TagColorG / 255f, config.TagColorB / 255f, 1f);
                        ImGui.ColorButton($"##tag_{file.ItemType}_{config.ConfigName}", swatch,
                            ImGuiColorEditFlags.NoTooltip, new Vector2(16f));
                        ImGui.SameLine();

                        string uniqueId = $"{config.ConfigName}##{file.ItemType}_{config.ConfigName}";
                        ImGui.Selectable(uniqueId);

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.BeginTooltip();
                            ImGui.Text($"{file.WeaponName} - {config.ConfigName}");
                            ImGui.Separator();
                            ImGui.Text($"Damage: {config.Damage}");
                            ImGui.Text($"Crit Rate: {config.CritRate}");
                            ImGui.Text($"Knockback: {config.knockBack}");
                            ImGui.Text($"Use Time: {config.UseTime}");
                            ImGui.Text($"Times To Shoot: {config.TimesToShoot}");
                            ImGui.Text($"Projectile: {config.Projectile}");
                            ImGui.Text($"Projectile Speed: {config.ProjectileSpeed}");
                            ImGui.TextDisabled(GetString("Left click: apply | Right click: delete"));
                            ImGui.EndTooltip();
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                        {
                            ApplyConfigToHeldItem(config);
                            statusText = "Applied " + ($"{config.ConfigName} to {HeldItem.Name}");
                        }

                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            WeaponConfigManager.DeleteConfig(file, config);
                            RefreshSavedConfigs();
                            statusText = ($"{config.ConfigName}") + " Was Deleted";
                        }
                    }
                    ImGui.Unindent();
                    ImGui.Spacing();
                }
            }
            else
            {
                ImGui.Text(GetString("No saved weapons found."));
            }
        }
    }
    private void ApplyConfigToHeldItem(WeaponConfig config)
    {
        HeldItem.prefix = config.Prefix;
        Main.NewText("Prefix set to " + HeldItem.prefix, 31, 184, 155);
        HeldItem.damage = config.Damage;
        Main.NewText("Damage set to " + HeldItem.damage,  31, 184, 155);
        HeldItem.useTime = config.UseTime;
        Main.NewText("Use Time set to " + HeldItem.useTime, 31, 184, 155);
        HeldItem.useAnimation = config.AnimationSpeed;
        HeldItem.scale = config.Size;
        Main.NewText("Size set to " + HeldItem.scale, 31, 184, 155);
        HeldItem.shoot = config.Projectile;
        HeldItem.shootSpeed = config.ProjectileSpeed;
        Main.NewText("Proj Speed set to " + HeldItem.shootSpeed, 1, 184, 155);
        HeldItem.pick = config.PickPower;
        HeldItem.axe = config.AxePower;
        HeldItem.hammer = config.HammerPower;
        HeldItem.crit = config.CritRate;
        Main.NewText("Crit rate set to " + HeldItem.crit, 31, 184, 155);
        HeldItem.knockBack = config.knockBack;
        HeldItem.useAmmo = config.UseAmmo;
        HeldItem.createTile = config.CreateTile;
        HeldItem.placeStyle = config.PlaceStyle;
        HeldItem.createWall = config.CreateWall;
        HeldItem.defense = config.Defense;
        HeldItem.useStyle = config.UseStyle;
        HeldItem.value = config.ValueCopper;
        HeldItem.buffType = config.Buff;
        HeldItem.buffTime = config.BuffTime;
        HeldItem.healLife = config.HealLife;
        HeldItem.healMana = config.HealMana;
        HeldItem.headSlot = config.HeadSlot;
        HeldItem.bodySlot = config.BodySlot;
        HeldItem.legSlot = config.LegSlot;
        HeldItem.vanity = config.Vanity;
        HeldItem.accessory = config.Accessory;
        HeldItem.consumable = config.Consumable;
        HeldItem.useTurn = config.UseTurn;
        HeldItem.noUseGraphic = config.BlockTurn;

        configNameBuffer = config.ConfigName;
        tagColor = new Vector3(config.TagColorR / 255f, config.TagColorG / 255f, config.TagColorB / 255f);

        timesToShootBuffer = config.TimesToShoot;
        Main.NewText("Times to shoot set to " + timesToShootBuffer, 31, 184, 155);
        lastHeldItemType = HeldItem.type;

        WeaponConfigManager.ItemTimesToShoot[config.ItemType] = config.TimesToShoot;
        WeaponConfigManager.ConfigName[config.ItemType] = config.ConfigName;
        WeaponConfigManager.TagColor[config.ItemType] = (config.TagColorR, config.TagColorG, config.TagColorB);

    }
    //Codigo Prueba

    private static string[] GetItemPrefixes()
    {
        string[] prefixes = new string[PrefixID.Count];
        for (var i = 0; i < PrefixID.Count; i++)
        {
            prefixes[i] = Lang.prefix[i].Value;
        }
        prefixes[0] = GetString("None");
        return prefixes;

    }
}