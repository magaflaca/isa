using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Input;
using Newtonsoft.Json;
using Terraria.UI;

namespace TerraAngel.Config;

public class ClientConfig
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = true)]
    public class UIConfigElementAttribute : Attribute
    {
        public readonly string Name;
        public readonly string Type;

        public UIConfigElementAttribute(string type, string name)
        {
            Name = name;
            Type = type;
        }
    }

    public static Config Settings = new Config();

    public class Config
    {
        [UIConfigElement("zh-CN", "主菜单按钮")]
        [UIConfigElement("en-US", "Main Menu Buttons")]
        public bool ShowMainMenuButtons = true; // Port: "TerraAngel Settings" and "TerraAngel Plugins" on the title screen.

        [UIConfigElement("zh-CN", "默认解除部分距离限制")]
        [UIConfigElement("en-US", "Default Remove Some Range Limits")]
        public bool DefaultRemoveViewRangeLimits = true;

        [UIConfigElement("zh-CN", "显示主窗口")]
        [UIConfigElement("en-US", "Show Stats Window")]
        public bool ShowStatsBar = false; // Port: it covers the top of the game's HUD; Windows menu shows it. A new name, so a value saved (or imported from TerraAngel) as on doesn't bring the bar back.

        [UIConfigElement("zh-CN", "显示终端")]
        [UIConfigElement("en-US", "Show Console Window")]
        public bool ShowConsoleWindow = true;

        [UIConfigElement("zh-CN", "默认启用无敌")]
        [UIConfigElement("en-US", "Default Anti-Hurt")]
        public bool DefaultAntiHurt = false;

        [UIConfigElement("zh-CN", "默认启用无限魔力")]
        [UIConfigElement("en-US", "Default Infinite Mana")]
        public bool DefaultInfiniteMana = false;

        [UIConfigElement("zh-CN", "默认启用无限仆从")]
        [UIConfigElement("en-US", "Default Infinite Minions")]
        public bool DefaultInfiniteMinions = false;

        [UIConfigElement("zh-CN", "默认开启旅行无敌")]
        [UIConfigElement("en-US", "Default creativeGodMode")]
        public bool DefaultcreativeGodMode = false;

        [UIConfigElement("zh-CN", "默认启用ESP透视")]
        [UIConfigElement("en-US", "Default ESP Draw Any")]
        public bool DefaultDrawAnyESP = false;

        [UIConfigElement("zh-CN", "ESP默认在地图上显示目标")]
        [UIConfigElement("en-US", "Default ESP On Map")]
        public bool DefaultMapESP = false;

        [UIConfigElement("zh-CN", "ESP默认追踪玩家")]
        [UIConfigElement("en-US", "Default ESP Tracers")]
        public bool DefaultPlayerESPTracers = false;

        [UIConfigElement("zh-CN", "ESP默认显示玩家碰撞箱")]
        [UIConfigElement("en-US", "Default ESP Player Hitboxes")]
        public bool DefaultPlayerESPBoxes = true;

        [UIConfigElement("zh-CN", "ESP默认显示NPC碰撞箱")]
        [UIConfigElement("en-US", "Default ESP NPC Hitboxes")]
        public bool DefaultNPCBoxes = false;

        [UIConfigElement("zh-CN", "ESP默认显示弹幕碰撞箱")]
        [UIConfigElement("en-US", "Default ESP Projectile Hitboxes")]
        public bool DefaultProjectileBoxes = false;

        [UIConfigElement("zh-CN", "ESP默认显示物品碰撞箱")]
        [UIConfigElement("en-US", "Default ESP Item Hitboxes")]
        public bool DefaultItemBoxes = false;

        [UIConfigElement("zh-CN", "默认显示手持物品")]
        [UIConfigElement("en-US", "Default Show Held Item")]
        public bool DefaultShowHeldItem = false;

        [UIConfigElement("zh-CN", "默认无视建造范围限制")]
        [UIConfigElement("en-US", "Default Infinite Reach")]
        public bool DefaultInfiniteReach = false;

        [UIConfigElement("zh-CN", "传送时发送混沌传送杖数据包")]
        [UIConfigElement("en-US", "Send Rod of Discord packet when teleporting")]
        public bool TeleportSendRODPacket = true;

        [UIConfigElement("zh-CN", "右键地图传送")]
        [UIConfigElement("en-US", "Right click on the map to teleport")]
        public bool RightClickOnMapToTeleport = true;

        [UIConfigElement("zh-CN", "Alt+右键 检查对象")]
        [UIConfigElement("en-US", "Alt + Right click on object to inspect")]
        public bool AltRightClickOnObjectToInspect = true;

        [UIConfigElement("zh-CN", "阻止星云数据包")]
        [UIConfigElement("en-US", "Disable Nebula Packet")]
        public bool DisableNebulaLagPacket = true;

        [UIConfigElement("zh-CN", "控制台自动滚动")]
        [UIConfigElement("en-US", "Console Auto Scroll")]
        public bool ConsoleAutoScroll = true;

        [UIConfigElement("zh-CN", "切换世界时清空聊天")]
        [UIConfigElement("en-US", "Clear chat on world changes")]
        public bool ClearChatThroughWorldChanges = false;

        [UIConfigElement("zh-CN", "关闭聊天面板时清空输入")]
        [UIConfigElement("en-US", "Clear chat input on close")]
        public bool ClearChatInputOnClose = false;

        [UIConfigElement("zh-CN", "默认启用弹幕预测")]
        [UIConfigElement("en-US", "Default Projectile Prediction")]
        public bool DefaultDrawActiveProjectilePrediction = true;

        [UIConfigElement("zh-CN", "默认预测友方弹幕轨迹")]
        [UIConfigElement("en-US", "Default Projectile Prediction Draw Friendly")]
        public bool DefaultDrawFriendlyProjectilePrediction = false;

        [UIConfigElement("zh-CN", "默认预测敌方弹幕轨迹")]
        [UIConfigElement("en-US", "Default Projectile Prediction Draw Hostile")]
        public bool DefaultDrawHostileProjectilePrediction = true;

        [UIConfigElement("zh-CN", "默认关闭血污")]
        [UIConfigElement("en-US", "Default Disable Gore")]
        public bool DefaultDisableGore = false;

        [UIConfigElement("zh-CN", "默认关闭粒子效果")]
        [UIConfigElement("en-US", "Default Disable Dust")]
        public bool DefaultDisableDust = false;

        [UIConfigElement("zh-CN", "Discord状态(和QQ状态一个东西)")]
        [UIConfigElement("en-US", "Discord Rich Presence")]
        public bool UseDiscordRPC = true;

        // for public release, set this to true
        // so that server owners can easily identify
        // mod users and ban them if they want to
        // [UIConfigElement("zh-CN", "向服务器摊牌你的古神身份 (实验性)")]
        // [UIConfigElement("en-US", "Tell server that you're using a modified client (Experimental)")]
        public bool BroadcastPresence = false; // Port: off by default, it sends hidden packets to the server every 10 s.

        [UIConfigElement("zh-CN", "显示详细工具提示")]
        [UIConfigElement("en-US", "Show Detailed Tooltips")]
        public bool ShowDetailedTooltips = false; // Port: off by default, it renames every item (chat included).

        [UIConfigElement("zh-CN", "忽略`Yoraiz0r的魔法`红眼效果")]
        [UIConfigElement("en-US", "Ignore super laggy visuals (Yorai eye)")]
        public bool IgnoreReLogicBullshit = true;

        [UIConfigElement("zh-CN", "保存控制台历史")]
        [UIConfigElement("en-US", "Save Console History")]
        public bool PreserveConsoleHistory = true;

        [UIConfigElement("zh-CN", "保存控制台状态")]
        [UIConfigElement("en-US", "Save Console State")]
        public bool ConsoleSaveInReplMode = true;

        [UIConfigElement("zh-CN", "按ESC关闭聊天栏而不打开背包")]
        [UIConfigElement("en-US", "Chat Replicates Vanilla Behavior")]
        public bool ChatVanillaInvetoryBehavior = true;

        // Port: off by default, the game's chat (and IsaClient's chat box) stay as they are.
        [UIConfigElement("en-US", "TerraAngel Chat (replaces the game's chat)")]
        public bool UseTerraAngelChat = false;

        [UIConfigElement("zh-CN", "默认关闭更新周边图格")]
        [UIConfigElement("en-US", "Default Disable Tile Framing")]
        public bool DefaultDisableTileFraming = false;

        [UIConfigElement("zh-CN", "ESP默认显示世界区块")]
        [UIConfigElement("en-US", "Default ESP Tile Sections")]
        public bool DefaultTileSections = false;

        [UIConfigElement("zh-CN", "默认全图高亮")]
        [UIConfigElement("en-US", "Default Full Bright")]
        public bool FullBrightDefaultValue = false;


        [UIConfigElement("zh-CN", "总是启用旅行菜单")]
        [UIConfigElement("en-US", "Always Enable Journey Menu")]
        public bool ForceEnableCreativeUI = false;

        [UIConfigElement("zh-CN", "总是启用旅行物品浏览器")]
        [UIConfigElement("en-US", "All Journey Items Available")]
        public bool ForceAllCreativeUnlocks = false;

        [UIConfigElement("zh-CN", "启用Steam")]
        [UIConfigElement("en-US", "Enable Steam")]
        public bool UseSteamSocialAPI = true;

        [UIConfigElement("zh-CN", "切换主窗口和终端")]
        [UIConfigElement("en-US", "Toggle UI")]
        public Keys ToggleUIVisibility = Keys.OemTilde;

        [UIConfigElement("zh-CN", "切换ESP追踪透视")]
        [UIConfigElement("en-US", "Toggle All ESP")]
        public Keys ToggleDrawAnyESP = Keys.End;

        [UIConfigElement("zh-CN", "切换可移动的状态窗 (没用)")]
        [UIConfigElement("en-US", "Toggle stats window being movable")]
        public Keys ToggleStatsWindowMovability = Keys.NumPad5;

        [UIConfigElement("zh-CN", "切换网络调试器UI")]
        [UIConfigElement("en-US", "Toggle Net Debugger UI")]
        public Keys ToggleNetDebugger = Keys.NumPad6;

        [UIConfigElement("zh-CN", "切换幽灵模式")]
        [UIConfigElement("en-US", "Toggle Noclip")]
        public Keys ToggleNoclip = Keys.None; // Port: F-keys are the client's; set one in the config if you want it.

        [UIConfigElement("zh-CN", "切换自由视角")]
        [UIConfigElement("en-US", "Toggle Freecam")]
        public Keys ToggleFreecam = Keys.None;

        [UIConfigElement("zh-CN", "切换全图高亮")]
        [UIConfigElement("en-US", "Toggle Fullbright")]
        public Keys ToggleFullBright = Keys.None;

        [UIConfigElement("zh-CN", "传送到光标位置")]
        [UIConfigElement("en-US", "Teleport to Cursor")]
        public Keys TeleportToCursor = Keys.None; // Port: Z teleported on any key press; IsaClient has its own Mouse TP key.

        [UIConfigElement("zh-CN", "切换UI样式编辑器")]
        [UIConfigElement("en-US", "Toggle Style Editor")]
        public Keys ToggleStyleEditor = Keys.NumPad8;

        [UIConfigElement("zh-CN", "世界编辑选择")]
        [UIConfigElement("en-US", "World Edit Select")]
        public Keys WorldEditSelectKey = Keys.F;

        [UIConfigElement("zh-CN", "显示玩家查看器")]
        [UIConfigElement("en-US", "Show Player Inspector")]
        public Keys ShowInspectorWindow = Keys.NumPad7;

        [UIConfigElement("zh-CN", "显示性能指示器")]
        [UIConfigElement("en-US", "Show Timing Metrics")]
        public Keys ShowTimingMetrics = Keys.NumPad9;

        [UIConfigElement("zh-CN", "拍摄地图截图")]
        [UIConfigElement("en-US", "Take Map Screenshot")]
        public Keys TakeMapScreenshot = Keys.None; // Port: F9 opens the IsaClient panel.

        [UIConfigElement("zh-CN", "启用快速物品浏览器 (Alt +")]
        [UIConfigElement("en-US", "Open Quick Item Browser (Alt +")]
        public Keys OpenFastItemBrowser = Keys.None; // Port: Alt+I opened it by accident (Alt stays down after Alt+Tab).

        [UIConfigElement("zh-CN", "建造者模式快速斜坡")]
        [UIConfigElement("en-US", "Builder Mode Quick Slope")]
        public Keys BuilderModeQuickSlope = Keys.Q;

        [UIConfigElement("zh-CN", "发送客户端UUID")]
        [UIConfigElement("en-US", "Send Client UUID")]
        public bool IsSendClientUUID = true;

        [UIConfigElement("zh-CN", "启用多语言翻译")]
        [UIConfigElement("en-US", "Follow Game Translation")]
        public bool IsFollowGameTranslation = true;

        [UIConfigElement("zh-CN", "启用内置调试命令")]
        [UIConfigElement("en-US", "Enable Built-In Debug Commands")]
        public bool IsEnableBuiltInDebugCommands = false;

        [UIConfigElement("zh-CN", "保留废案物品(例如最初分型)")]
        [UIConfigElement("en-US", "Preserve Deprecated Items (e.g. First Fractal)")]
        public bool IsPreserveDeprecatedItems = false;

        public string CustomClientHello = "";

        public int ConsoleHistoryLimit = 5000;
        public int ChatHistoryLimit = 3000;
        public int ChatMessageLimit = 600;

        public List<string>? ConsoleHistorySave = new List<string>();

        [JsonIgnore] public List<string> ConsoleHistory = new List<string>();

        public int ConsoleUndoStackSize = 3000;

        public float FullBrightBrightness = 0.7f;
        public Color TracerColor = new Color(0f, 0f, 1f);
        public Color LocalBoxPlayerColor = new Color(0f, 1f, 0f);
        public Color OtherBoxPlayerColor = new Color(1f, 0f, 0f);
        public Color OtherTerraAngelUserColor = new Color(1f, 0.5f, 0.3f);
        public Color NPCBoxColor = new Color(1f, 0f, 1f);
        public Color NPCNetOffsetBoxColor = new Color(0f, 0f, 0f);
        public Color ProjectileBoxColor = new Color(1f, 0f, 1f);
        public Color ItemBoxColor = new Color(0.9f, 0.2f, 0.6f);
        public Color FriendlyProjectilePredictionDrawColor = new Color(0f, 1f, 0f);
        public Color HostileProjectilePredictionDrawColor = new Color(1f, 0f, 0f);
        public int ProjectilePredictionMaxStepCount = 1500;

        public float ChatWindowTransperencyActive = 0.5f;
        public float ChatWindowTransperencyInactive = 0.0f;
        public bool ChatAutoScroll = true;
        public int framesForMessageToBeVisible = 600;

        public bool AutoFishAcceptItems = true;
        public bool AutoFishAcceptAllItems = true;
        public bool AutoFishAcceptQuestFish = true;
        public bool AutoFishAcceptCrates = true;
        public bool AutoFishAcceptNormal = true;
        public bool AutoFishAcceptCommon = true;
        public bool AutoFishAcceptUncommon = true;
        public bool AutoFishAcceptRare = true;
        public bool AutoFishAcceptVeryRare = true;
        public bool AutoFishAcceptLegendary = true;
        public bool AutoFishAcceptNPCs = true;
        public int AutoFishFrameCountRandomizationMin = 10;
        public int AutoFishFrameCountRandomizationMax = 50;

        public float AutoAttackMinTargetRange = 800f;
        public bool AutoAttackMouseBounding = false;
        public bool AutoAttackShowMouseRange = true;
        public bool AutoAttackRequireLineOfSight = true;
        public bool AutoAttackVelocityPrediction = true;
        public float AutoAttackVelocityPredictionScaling = 0.1f;  // 从 0.2269f 降低到 0.1f
        public bool AutoAttackTargetHostileNPCs = true;
        public bool AutoAttackFavorBosses = true;
        public int AutoAttackTargetPriority = 0;
        public bool AutoAttackTargetPlayers = false;
        public bool AutoAttackOnlyPvPPlayers = false;
        public string AutoAttackTargetPlayerName = "";

        public float StatsWindowHoveredTransperency = 0.65f;

        public int LightingBlurPassCount = 4;

        public int MapScreenshotPixelsPerTile = 4;


        public ClientUIConfig UIConfig = new ClientUIConfig();

        [JsonIgnore]
        public List<string> PluginsToEnable
        {
            get
            {
                // Port: IsaClient's plugin loader keeps which plugins are on.
                return new List<string>();
            }
            set
            {
            }
        }

        [JsonProperty("PluginsToEnable")] public List<string> pluginsToEnable;

        public List<UIElement> GetUITexts()
        {
            SortedList<string, UIElement> elements = new SortedList<string, UIElement>();
            List<FieldInfo> fields = typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Instance).ToList();
            CultureInfo culture = Util.CurrentCulture;
            for (int i = 0; i < fields.Count; i++)
            {
                FieldInfo field = fields[i];
                var languagesAttribute = field.GetCustomAttributes<UIConfigElementAttribute>().ToArray();
                var currentLanguage = Settings.IsFollowGameTranslation
                    ? languagesAttribute.FirstOrDefault(x => x.Type == culture.Name) ??
                      languagesAttribute.FirstOrDefault(x => x.Type == "en-US")
                    : languagesAttribute.FirstOrDefault(x => x.Type == "en-US");
                if (currentLanguage != null)
                {
                    string name = currentLanguage.Name;

                    // Port: TerraAngel's settings menu (title screen > TerraAngel Settings, see MenuScreens).
                    if (field.FieldType == typeof(bool))
                    {
                        elements[name] = new TerraAngel.UI.TerrariaUI.UITextCheckbox(name, () => (bool)(field.GetValue(this) ?? false), x => field.SetValue(this, x));
                    }
                    else if (field.FieldType == typeof(Keys))
                    {
                        elements["\uFFFF" + name] = new TerraAngel.UI.TerrariaUI.UIKeySelect(name, () => (Keys)(field.GetValue(this) ?? Keys.None), x => field.SetValue(this, x));
                    }
                }
            }


            return elements.Values.ToList();
        }
    }


    //public class VectorConverter : JsonConverter
    //{
    //    public override bool CanConvert(Type objectType)
    //    {
    //        return objectType == typeof(Vector2) || objectType == typeof(Vector3) || objectType == typeof(Vector4);
    //    }
    //
    //    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    //    {
    //        if (objectType == typeof(Vector2)) return serializer.Deserialize<System.Numerics.Vector2>(reader).ToXNA();
    //        if (objectType == typeof(Vector3)) return serializer.Deserialize<System.Numerics.Vector3>(reader).ToXNA();
    //        if (objectType == typeof(Vector4)) return serializer.Deserialize<System.Numerics.Vector4>(reader).ToXNA();
    //        return existingValue;
    //    }
    //
    //    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    //    {
    //        if (value is Vector2 vec2) serializer.Serialize(writer, vec2.ToNumerics());
    //        if (value is Vector3 vec3) serializer.Serialize(writer, vec3.ToNumerics());
    //        if (value is Vector4 vec4) serializer.Serialize(writer, vec4.ToNumerics());
    //    }
    //}

    public static JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
    {
        Converters = new List<JsonConverter>()
        {
            //    new VectorConverter(),
        },
        Formatting = Formatting.Indented,
    };

    public static void BeforeWrite()
    {
        // Port: what you turn on or off in the tools is kept, so it's the same after a restart.
        SaveCringeValues();
        Settings.pluginsToEnable = Settings.PluginsToEnable;
        Settings.UIConfig.Get();
        if (Settings.PreserveConsoleHistory)
            Settings.ConsoleHistorySave = Settings.ConsoleHistory;
        // Port: the lighting blur pass count is a TerraAngel addition to the lighting engine.
    }

    private static object FileLock = new object();

    public static Task WriteToFile()
    {
        lock (FileLock)
        {
            BeforeWrite();
            string s = JsonConvert.SerializeObject(Settings, SerializerSettings);
            DirectoryUtility.TryCreateParentDirectory(ClientLoader.ConfigPath);
            using (FileStream fs = new FileStream(ClientLoader.ConfigPath, FileMode.OpenOrCreate))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                fs.SetLength(bytes.Length);
                fs.Write(bytes, 0, bytes.Length);
                fs.Close();
            }

            Settings.ConsoleHistorySave = null;
        }

        return Task.CompletedTask;
    }

    public static void AfterRead()
    {
        if (Settings.PreserveConsoleHistory && Settings.ConsoleHistorySave is not null)
            Settings.ConsoleHistory = Settings.ConsoleHistorySave;
        else
            Settings.ConsoleHistorySave = null;
    }

    public static void AfterReadLater()
    {
        Settings.UIConfig.Set();
    }

    public static void ReadFromFile()
    {
        if (!File.Exists(ClientLoader.ConfigPath))
        {
            WriteToFile();
        }

        lock (FileLock)
        {
            string s = "";
            using (FileStream fs = new FileStream(ClientLoader.ConfigPath, FileMode.OpenOrCreate))
            {
                byte[] buffer = new byte[fs.Length];
                fs.Read(buffer, 0, buffer.Length);
                s = Encoding.UTF8.GetString(buffer);
                fs.Close();
            }

            Settings = JsonConvert.DeserializeObject<Config>(s, SerializerSettings) ?? new Config();
            AfterRead();
        }
    }

    private static float SaveTimer;

    public static void Update()
    {
        SaveTimer -= Time.UpdateDeltaTime;
        if (SaveTimer <= 0.0f)
        {
            SaveTimer = 5.0f;

            Task.Run(WriteToFile);

        }
    }

    delegate ref float FuncRefFloat();

    delegate ref Vector2 FuncRefVector2();

    delegate ref Vector3 FuncRefVector3();

    delegate ref Vector4 FuncRefVector4();

    delegate ref bool FuncRefBool();

    /// <summary>Port: the tools that take their start values from the config (to write them back).</summary>
    private static readonly List<Tool> CringeTools = new List<Tool>();

    /// <summary>Port: each tool's current values back into the config fields they start from.</summary>
    public static void SaveCringeValues()
    {
        Tool[] tools;
        lock (CringeTools)
        {
            tools = CringeTools.ToArray();
        }
        foreach (Tool tool in tools)
        {
            try
            {
                Type type = tool.GetType();
                foreach (FieldInfo field in type.GetFields().Where(x => Attribute.IsDefined(x, typeof(DefaultConfigValueAttribute))))
                {
                    Store(field.GetCustomAttribute<DefaultConfigValueAttribute>(), field.FieldType, field.GetValue(tool));
                }
                foreach (PropertyInfo property in type.GetProperties().Where(x => Attribute.IsDefined(x, typeof(DefaultConfigValueAttribute))))
                {
                    MethodInfo? getMethod = property.GetMethod;
                    if (getMethod is null)
                        continue;
                    Type t = property.PropertyType;
                    if (!t.IsByRef)
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), t, property.GetValue(tool));
                    }
                    else if (t == typeof(float).MakeByRefType())
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), typeof(float), getMethod.CreateDelegate<FuncRefFloat>(tool)());
                    }
                    else if (t == typeof(Vector2).MakeByRefType())
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), typeof(Vector2), getMethod.CreateDelegate<FuncRefVector2>(tool)());
                    }
                    else if (t == typeof(Vector3).MakeByRefType())
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), typeof(Vector3), getMethod.CreateDelegate<FuncRefVector3>(tool)());
                    }
                    else if (t == typeof(Vector4).MakeByRefType())
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), typeof(Vector4), getMethod.CreateDelegate<FuncRefVector4>(tool)());
                    }
                    else if (t == typeof(bool).MakeByRefType())
                    {
                        Store(property.GetCustomAttribute<DefaultConfigValueAttribute>(), typeof(bool), getMethod.CreateDelegate<FuncRefBool>(tool)());
                    }
                }
            }
            catch
            {
                // One tool that can't be read doesn't stop the others from being kept.
            }
        }
    }

    private static void Store(DefaultConfigValueAttribute? value, Type type, object? current)
    {
        if (value is null || current is null)
            return;
        FieldInfo? configField = typeof(Config).GetField(value.FieldName, BindingFlags.Public | BindingFlags.Instance);
        if (configField is not null && configField.FieldType == type)
        {
            configField.SetValue(Settings, current);
        }
    }

    public static void SetDefaultCringeValues(Tool cringe)
    {
        lock (CringeTools)
        {
            if (!CringeTools.Contains(cringe))
                CringeTools.Add(cringe);
        }
        Type type = cringe.GetType();
        foreach (FieldInfo field in type.GetFields()
                     .Where(x => Attribute.IsDefined(x, typeof(DefaultConfigValueAttribute))))
        {
            DefaultConfigValueAttribute? value =
                (DefaultConfigValueAttribute?)Attribute.GetCustomAttribute(field, typeof(DefaultConfigValueAttribute));
            if (value is null) throw new NullReferenceException("No attribute L");

            FieldInfo? configField =
                typeof(Config).GetField(value.FieldName, BindingFlags.Public | BindingFlags.Instance);
            if (configField is null) throw new Exception($"Could not find field {value.FieldName}");

            field.SetValue(cringe, configField.GetValue(Settings));
        }

        foreach (PropertyInfo property in type.GetProperties()
                     .Where(x => Attribute.IsDefined(x, typeof(DefaultConfigValueAttribute))))
        {
            bool refType = false;
            if (!property.CanWrite)
            {
                if (property.CanRead && property.GetMethod is not null && property.GetMethod.ReturnType.IsByRef)
                {
                    refType = true;
                }
                else
                {
                    throw new Exception($"Property {property.Name} cannot be written to");
                }
            }

            DefaultConfigValueAttribute? value =
                (DefaultConfigValueAttribute?)Attribute.GetCustomAttribute(property,
                    typeof(DefaultConfigValueAttribute));
            if (value is null) throw new NullReferenceException("No attribute L");

            FieldInfo? configField =
                typeof(Config).GetField(value.FieldName, BindingFlags.Public | BindingFlags.Instance);
            if (configField is null) throw new Exception($"Could not find field {value.FieldName}");

            if (!refType) property.SetValue(cringe, configField.GetValue(Settings));

            // this is a shitty solution to the problem of dotnet not having an easy way to set ref properties :(
            else
            {
                object? obj = configField.GetValue(Settings);
                Type t = property.PropertyType;
                MethodInfo? getMethod = property.GetMethod;
                if (getMethod is null)
                    continue;

                if (t == typeof(float).MakeByRefType())
                {
                    getMethod.CreateDelegate<FuncRefFloat>(cringe)() = (float)obj;
                }
                else if (t == typeof(Vector2).MakeByRefType())
                {
                    getMethod.CreateDelegate<FuncRefVector2>(cringe)() = (Vector2)obj;
                }
                else if (t == typeof(Vector3).MakeByRefType())
                {
                    getMethod.CreateDelegate<FuncRefVector3>(cringe)() = (Vector3)obj;
                }
                else if (t == typeof(Vector4).MakeByRefType())
                {
                    getMethod.CreateDelegate<FuncRefVector4>(cringe)() = (Vector4)obj;
                }
                else if (t == typeof(bool).MakeByRefType())
                {
                    getMethod.CreateDelegate<FuncRefBool>(cringe)() = (bool)obj;
                }
            }
        }
    }
}