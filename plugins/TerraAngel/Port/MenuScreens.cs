using System.Diagnostics;
using IsaClient.Plugins;
using Microsoft.Xna.Framework.Input;
using Terraria.Audio;
using Terraria.GameContent.UI.Elements;
using Terraria.Localization;
using Terraria.UI;

namespace TerraAngel.UI.TerrariaUI;

/// <summary>
/// TerraAngel's two title-screen menus, as TerraAngel had them: "TerraAngel Settings" (its options, as Terraria
/// toggles and key pickers) and "TerraAngel Plugins" (every plugin in IsaClient's Plugins folder, on or off, rescan,
/// open the folder). They're added above Settings while "Main Menu Buttons" is on (it is by default).
/// </summary>
public static class MenuScreens
{
    private static ConfigUI? _config;

    private static PluginUI? _plugins;

    private static MainMenuEntries.Entry? _configEntry;

    private static MainMenuEntries.Entry? _pluginsEntry;

    public static void Add()
    {
        Remove();
        _configEntry = MainMenuEntries.Add(() => GetString("TerraAngel Settings"), () => Open(_config ??= new ConfigUI()), () => ClientConfig.Settings.ShowMainMenuButtons);
        _pluginsEntry = MainMenuEntries.Add(() => GetString("TerraAngel Plugins"), () => Open(_plugins ??= new PluginUI()), () => ClientConfig.Settings.ShowMainMenuButtons);
    }

    public static void Remove()
    {
        if (_configEntry != null)
        {
            MainMenuEntries.Remove(_configEntry);
        }
        if (_pluginsEntry != null)
        {
            MainMenuEntries.Remove(_pluginsEntry);
        }
        _configEntry = _pluginsEntry = null;
    }

    private static void Open(UIState state)
    {
        Main.menuMode = 888;
        Main.MenuUI.SetState(state);
    }

    internal static void Back()
    {
        Main.menuMode = 0;
        SoundEngine.PlaySound(SoundID.MenuClose);
    }

    internal static UITextPanel<string> Button(string text, float widthPart, float hAlign, UIElement.MouseEvent onClick)
    {
        UITextPanel<string> button = new UITextPanel<string>(text, 0.7f, large: true)
        {
            Width = new StyleDimension(-10f, widthPart),
            Height = { Pixels = 50 },
            Top = { Pixels = -45 },
            VAlign = 1f,
            HAlign = hAlign
        };
        button.OnMouseOver += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            button.BackgroundColor = new Color(73, 94, 171);
            button.BorderColor = Colors.FancyUIFatButtonMouseOver;
        };
        button.OnMouseOut += (_, _) =>
        {
            button.BackgroundColor = new Color(63, 82, 151) * 0.8f;
            button.BorderColor = Color.Black;
        };
        button.OnLeftClick += onClick;
        return button;
    }

    /// <summary>The usual Terraria menu frame: a panel with a scrolling list and a row of buttons under it.</summary>
    internal static UIList Frame(UIState state, string title, out UIElement root)
    {
        root = new UIElement
        {
            Width = { Precent = 0.8f },
            MaxWidth = { Pixels = 600 },
            Top = { Pixels = 220 },
            Height = { Pixels = -220, Precent = 1f },
            HAlign = 0.5f
        };
        UIPanel panel = new UIPanel
        {
            Width = { Precent = 1f },
            Height = { Pixels = -110, Precent = 1f },
            BackgroundColor = new Color(33, 43, 79) * 0.8f
        };
        root.Append(panel);
        UIList list = new UIList
        {
            Width = { Pixels = -25, Precent = 1f },
            Height = { Precent = 1f },
            ListPadding = 5f
        };
        list.ManualSortMethod = _ => { };
        panel.Append(list);
        UIScrollbar bar = new UIScrollbar
        {
            Height = { Pixels = -12f, Precent = 1f },
            Top = { Pixels = 6 },
            HAlign = 1f
        };
        bar.SetView(100f, 1000f);
        panel.Append(bar);
        list.SetScrollbar(bar);
        UITextPanel<string> header = new UITextPanel<string>(title, 0.8f, large: true)
        {
            HAlign = 0.5f,
            Top = { Pixels = -45 },
            BackgroundColor = new Color(73, 94, 171)
        };
        header.SetPadding(15f);
        root.Append(header);
        state.Append(root);
        return list;
    }
}

/// <summary>TerraAngel Settings: its options (on/off and keys), saved as they change.</summary>
public class ConfigUI : UIState, IHaveBackButtonCommand
{
    private UIList? _list;

    public override void OnInitialize()
    {
        _list = MenuScreens.Frame(this, GetString("TerraAngel Settings"), out UIElement root);
        root.Append(MenuScreens.Button(Language.GetTextValue("UI.Back"), 1f, 0.5f, (_, _) => HandleBackButtonUsage()));
    }

    public override void OnActivate()
    {
        if (_list == null)
        {
            return;
        }
        _list.Clear();
        foreach (UIElement element in ClientConfig.Settings.GetUITexts())
        {
            _list.Add(element);
        }
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
        if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
        {
            HandleBackButtonUsage();
        }
    }

    public void HandleBackButtonUsage()
    {
        ClientConfig.WriteToFile();
        MenuScreens.Back();
    }
}

/// <summary>TerraAngel Plugins: every plugin in the Plugins folder; click one to turn it on or off.</summary>
public class PluginUI : UIState, IHaveBackButtonCommand
{
    private UIList? _list;

    private int _ticks;

    private string _shownKey = "";

    public override void OnInitialize()
    {
        _list = MenuScreens.Frame(this, GetString("TerraAngel Plugins"), out UIElement root);
        root.Append(MenuScreens.Button(Language.GetTextValue("UI.Back"), 1f / 3f, 0f, (_, _) => HandleBackButtonUsage()));
        root.Append(MenuScreens.Button(GetString("Rescan"), 1f / 3f, 0.5f, (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            PluginLoader.Scan();
            _shownKey = "";
        }));
        root.Append(MenuScreens.Button(GetString("Open Plugins Folder"), 1f / 3f, 1f, (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            System.IO.Directory.CreateDirectory(PluginLoader.PluginsFolder);
            Process.Start(new ProcessStartInfo { FileName = PluginLoader.PluginsFolder, UseShellExecute = true });
        }));
    }

    public override void OnActivate()
    {
        _shownKey = "";
    }

    public override void Update(GameTime gameTime)
    {
        // The list is made again when a plugin appears or goes (status texts update by themselves).
        if (_list != null && (++_ticks % 30 == 0 || _shownKey.Length == 0))
        {
            string key = string.Join("|", PluginLoader.Entries.Select(e => e.Key)) + "#";
            if (key != _shownKey)
            {
                _shownKey = key;
                _list.Clear();
                foreach (PluginEntry entry in PluginLoader.Entries)
                {
                    _list.Add(new UIPluginRow(entry));
                }
            }
        }
        base.Update(gameTime);
        if (Main.keyState.IsKeyDown(Keys.Escape) && !Main.oldKeyState.IsKeyDown(Keys.Escape))
        {
            HandleBackButtonUsage();
        }
    }

    public void HandleBackButtonUsage()
    {
        MenuScreens.Back();
    }
}

/// <summary>One plugin: its name and state (green on, red off); a click turns it on or off.</summary>
public class UIPluginRow : UIPanel
{
    private readonly PluginEntry _entry;

    private readonly UIText _name;

    private readonly UIText _state;

    public UIPluginRow(PluginEntry entry)
    {
        _entry = entry;
        Width = new StyleDimension(0, 1f);
        Height = new StyleDimension(40, 0f);
        BorderColor = Color.Black;
        BackgroundColor = new Color(20, 24, 44) * 0.9f;
        _name = new UIText(entry.Name, 0.9f) { HAlign = 0f, VAlign = 0.5f };
        _state = new UIText("", 0.9f) { HAlign = 1f, VAlign = 0.5f };
        Append(_name);
        Append(_state);
        OnLeftClick += (_, _) =>
        {
            if (_entry.Building)
            {
                return;
            }
            PluginLoader.SetEnabled(_entry, !PluginLoader.IsEnabled(_entry));
            SoundEngine.PlaySound(SoundID.MenuTick);
        };
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        bool on = PluginLoader.IsEnabled(_entry);
        string state;
        Color color;
        if (_entry.Building)
        {
            state = GetString("Building...");
            color = Color.Yellow;
        }
        else if (!string.IsNullOrEmpty(_entry.Error))
        {
            state = GetString("Error");
            color = Color.OrangeRed;
        }
        else
        {
            state = on ? GetString("Enabled") : GetString("Disabled");
            color = on ? Color.Green : Color.Red;
        }
        _state.SetText(state);
        _name.SetText(_entry.Name);
        _name.TextColor = _state.TextColor = color;
        base.Draw(spriteBatch);
    }

    public override void MouseOver(UIMouseEvent evt)
    {
        base.MouseOver(evt);
        if (!string.IsNullOrEmpty(_entry.Error))
        {
            Main.instance.MouseText(_entry.Error);
        }
    }
}

/// <summary>An on/off option (green on, red off).</summary>
public class UITextCheckbox : UIPanel
{
    private readonly UIText _name;

    private readonly UIText _value;

    private readonly Func<bool> _get;

    public UITextCheckbox(string name, Func<bool> get, Action<bool> set, float textScale = .9f)
    {
        _get = get;
        Width = new StyleDimension(0, 1f);
        Height = new StyleDimension(40, 0f);
        BorderColor = Color.Black;
        BackgroundColor = new Color(20, 24, 44) * 0.9f;
        _name = new UIText(name, textScale) { HAlign = 0f, VAlign = 0.5f };
        _value = new UIText("", textScale) { HAlign = 1f, VAlign = 0.5f };
        OnLeftClick += (_, _) =>
        {
            set(!get());
            ClientConfig.WriteToFile();
            SoundEngine.PlaySound(SoundID.MenuTick);
        };
        Append(_name);
        Append(_value);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        bool on = _get();
        _value.SetText(on ? GetString("On") : GetString("Off"));
        _name.TextColor = _value.TextColor = on ? Color.Green : Color.Red;
        base.Draw(spriteBatch);
    }
}

/// <summary>A key: click, then press the key (click again to unbind).</summary>
public class UIKeySelect : UIPanel
{
    private readonly UIText _key;

    private readonly Func<Keys> _get;

    private readonly Action<Keys> _set;

    private bool _waiting;

    public UIKeySelect(string name, Func<Keys> get, Action<Keys> set, float textScale = .9f)
    {
        _get = get;
        _set = set;
        Width = new StyleDimension(0, 1f);
        Height = new StyleDimension(40, 0f);
        BorderColor = Color.Black;
        BackgroundColor = new Color(20, 24, 44) * 0.9f;
        Append(new UIText(name, textScale) { HAlign = 0f, VAlign = 0.5f });
        _key = new UIText("", textScale) { HAlign = 1f, VAlign = 0.5f };
        Append(_key);
        OnLeftClick += (_, _) =>
        {
            SoundEngine.PlaySound(SoundID.MenuTick);
            _waiting = !_waiting;
            if (!_waiting && _get() != Keys.None)
            {
                _set(Keys.None);
                ClientConfig.WriteToFile();
            }
        };
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (_waiting)
        {
            foreach (Keys key in Main.keyState.GetPressedKeys())
            {
                if (key != Keys.None && !Main.oldKeyState.IsKeyDown(key))
                {
                    _set(key);
                    _waiting = false;
                    ClientConfig.WriteToFile();
                    SoundEngine.PlaySound(SoundID.MenuTick);
                    break;
                }
            }
        }
        Keys current = _get();
        _key.SetText(_waiting ? GetString("<Press Key>") : ((current == Keys.None) ? GetString("<Unbound>") : current.ToString()));
        _key.TextColor = _waiting ? new Color(2, 255, 2) : ((current == Keys.None) ? Color.DimGray : Color.White);
        base.Draw(spriteBatch);
    }
}
