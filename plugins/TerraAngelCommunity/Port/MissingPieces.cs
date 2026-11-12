using Terraria.GameContent;
using TerraAngel.Tools.Inspector;
using TerraAngel.Tools.Developer;
using XVector2 = Microsoft.Xna.Framework.Vector2;

// Classes the fork's code uses that were not shared with it, written again from how the code uses them.

namespace TerraAngel.Tools.Developer
{
    /// <summary>A buff browser (its own tab in Main Window > Tools, beside Butcher): click a buff to get it for <see cref="Time"/> seconds.</summary>
    public class BuffBrowserTool : Tool
    {
        public override string Name => GetString("Buff Browser");

        public override ToolTabs Tab => ToolTabs.NewTab;

        /// <summary>Seconds a buff lasts when given.</summary>
        public int Time = 240;

        private string _search = "";

        private static readonly Dictionary<int, IntPtr> _icons = new Dictionary<int, IntPtr>();

        public override void DrawUI(ImGuiIOPtr io)
        {
            ImGui.InputText("Search##buffs", ref _search, 64);
            ImGui.InputInt("Seconds##buffs", ref Time);
            Time = Math.Max(1, Time);
            if (Main.gameMenu)
            {
                return;
            }
            // The grid fills the rest of the tab.
            ImGui.BeginChild("buffgrid", new NVector2(0f, Math.Max(220f, ImGui.GetContentRegionAvail().Y)), ImGuiChildFlags.Border);
            float width = ImGui.GetContentRegionAvail().X;
            float x = 0f;
            string search = _search.ToLowerInvariant();
            for (int id = 1; id < BuffID.Count; id++)
            {
                string name = Lang.GetBuffName(id);
                if (string.IsNullOrEmpty(name) || (search.Length > 0 && !name.ToLowerInvariant().Contains(search)))
                {
                    continue;
                }
                if (!_icons.TryGetValue(id, out IntPtr icon))
                {
                    icon = ClientLoader.MainRenderer.BindTexture(TextureAssets.Buff[id].Value);
                    _icons[id] = icon;
                }
                if (x > 0f && x + 36f > width)
                {
                    x = 0f;
                }
                else if (x > 0f)
                {
                    ImGui.SameLine();
                }
                if (ImGui.ImageButton("buff" + id, icon, new NVector2(28f, 28f)))
                {
                    Main.LocalPlayer.AddBuff(id, Time * 60);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(name + " (" + id + ")");
                }
                x += 36f;
            }
            ImGui.EndChild();
        }
    }
}

namespace TerraAngel.Graphics
{
    /// <summary>
    /// Pictures of the local player (live) and of saved looks (Player Editor presets), drawn into textures before the
    /// game draws (the Community plugin calls <see cref="Render"/> then).
    /// </summary>
    public static class PlayerPortraitCache
    {
        public const int Width = 64;

        public const int Height = 96;

        private static RenderTarget2D _live;

        private static IntPtr _liveId;

        private static int _liveTimer;

        private static readonly Dictionary<string, (RenderTarget2D Target, IntPtr Id)> _presets = new Dictionary<string, (RenderTarget2D, IntPtr)>();

        private static readonly Queue<PlayerPreset> _wanted = new Queue<PlayerPreset>();

        private static bool _liveWanted;

        public static IntPtr GetLive()
        {
            _liveWanted = true;
            return _liveId;
        }

        public static IntPtr Get(PlayerPreset preset)
        {
            if (preset == null)
            {
                return IntPtr.Zero;
            }
            if (_presets.TryGetValue(preset.PresetName, out var picture))
            {
                return picture.Id;
            }
            if (!_wanted.Contains(preset))
            {
                _wanted.Enqueue(preset);
            }
            return IntPtr.Zero;
        }

        public static void Invalidate()
        {
            foreach (var picture in _presets.Values)
            {
                ClientLoader.MainRenderer.UnbindTexture(picture.Id);
                picture.Target.Dispose();
            }
            _presets.Clear();
        }

        /// <summary>Draws what was asked for (the client restores the render target and SpriteBatch after).</summary>
        public static void Render()
        {
            if (Main.gameMenu || Main.LocalPlayer == null)
            {
                return;
            }
            GraphicsDevice device = Main.instance.GraphicsDevice;
            if (_liveWanted && --_liveTimer <= 0)
            {
                _liveTimer = 6;
                _liveWanted = false;
                if (_live == null)
                {
                    _live = new RenderTarget2D(device, Width, Height);
                    _liveId = ClientLoader.MainRenderer.BindTexture(_live);
                }
                Draw(device, _live, Main.LocalPlayer);
            }
            if (_wanted.Count > 0)
            {
                PlayerPreset preset = _wanted.Dequeue();
                Player copy = Main.LocalPlayer.SerializedClone();
                ApplyLook(preset, copy);
                RenderTarget2D target = new RenderTarget2D(device, Width, Height);
                Draw(device, target, copy);
                _presets[preset.PresetName] = (target, ClientLoader.MainRenderer.BindTexture(target));
            }
        }

        /// <summary>A preset's look on a copy of the player (the editor's own Apply also tells the server, for the real one).</summary>
        private static void ApplyLook(PlayerPreset preset, Player player)
        {
            player.skinVariant = Math.Clamp(preset.SkinVariant, 0, PlayerVariantID.Count - 1);
            player.hair = preset.Hair;
            player.hairDye = preset.HairDye;
            player.hairColor = preset.HairColor.ToColor();
            player.eyeColor = preset.EyeColor.ToColor();
            player.skinColor = preset.SkinColor.ToColor();
            player.shirtColor = preset.ShirtColor.ToColor();
            player.underShirtColor = preset.UnderShirtColor.ToColor();
            player.pantsColor = preset.PantsColor.ToColor();
            player.shoeColor = preset.ShoeColor.ToColor();
        }

        /// <summary>Full light for the portraits: the world's light where the copy is drawn made them dark or black.</summary>
        private sealed class PortraitLight : Terraria.Graphics.Light.ILightingEngine
        {
            public void Rebuild()
            {
            }

            public void AddLight(int x, int y, Microsoft.Xna.Framework.Vector3 color)
            {
            }

            public void ProcessArea(Microsoft.Xna.Framework.Rectangle area)
            {
            }

            public Microsoft.Xna.Framework.Vector3 GetColor(int x, int y) => Microsoft.Xna.Framework.Vector3.One;

            public void Clear()
            {
            }
        }

        private static readonly PortraitLight _light = new PortraitLight();

        private static readonly System.Reflection.FieldInfo _activeEngine = HarmonyLib.AccessTools.Field(typeof(Lighting), "_activeEngine");

        /// <summary>Like the character select screen: full light, no item in the hand.</summary>
        /// <summary>
        /// Draws the player twice: measured in game, the first player drawn after the game's own frame comes out shrunk
        /// into a corner (8 pixels of it), the one drawn right after it is whole and in place, and replaces it.
        /// </summary>
        private static void Draw(GraphicsDevice device, RenderTarget2D target, Player player)
        {
            DrawOnce(device, target, player);
            DrawOnce(device, target, player);
        }

        private static void DrawOnce(GraphicsDevice device, RenderTarget2D target, Player player)
        {
            Viewport viewport = device.Viewport;
            device.SetRenderTarget(target);
            // The sprites are placed for the viewport: the game's (the whole screen) was still set at this point, so the
            // player came out squeezed into a few pixels of the corner.
            Viewport portrait = new Viewport(0, 0, target.Width, target.Height);
            device.Viewport = portrait;
            device.Clear(Microsoft.Xna.Framework.Color.Transparent);
            object engine = _activeEngine?.GetValue(null);
            float brightness = Lighting.GlobalBrightness;
            int selected = player.selectedItem;
            Item held = player.inventory[selected];
            XVector2 position = player.position;
            // States that make the game draw a player faint or not at all (invisibility, stealth, hit blinking, shimmer).
            bool invis = player.invis;
            float stealth = player.stealth;
            int immuneAlpha = player.immuneAlpha;
            float shimmer = player.shimmerTransparency;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone, null, Microsoft.Xna.Framework.Matrix.Identity);
            try
            {
                _activeEngine?.SetValue(null, _light);
                Lighting.GlobalBrightness = 1f;
                player.inventory[selected] = new Item();
                player.invis = false;
                player.stealth = 1f;
                player.immuneAlpha = 0;
                player.shimmerTransparency = 0f;
                player.PlayerFrame();
                XVector2 at = Main.screenPosition + new XVector2(Width / 2f - player.width / 2f, Height / 2f - player.height / 2f);
                Main.PlayerRenderer.DrawPlayer(Main.Camera, player, at, 0f, XVector2.Zero, 0f, 1f);
            }
            finally
            {
                player.inventory[selected] = held;
                player.position = position;
                player.invis = invis;
                player.stealth = stealth;
                player.immuneAlpha = immuneAlpha;
                player.shimmerTransparency = shimmer;
                Lighting.GlobalBrightness = brightness;
                if (engine != null)
                {
                    _activeEngine.SetValue(null, engine);
                }
                device.Viewport = portrait;
                Main.spriteBatch.End();
                device.SetRenderTarget(null);
                try
                {
                    device.Viewport = viewport;
                }
                catch (ArgumentException)
                {
                    // It belonged to another target: the backbuffer's own viewport (set by SetRenderTarget) stays.
                }
            }
        }
    }
}

namespace TerraAngel.Tools.Inspector
{
    /// <summary>Options the fork added to the player inspector (its Player Editor saves them).</summary>
    public static class PlayerInspectorExtras
    {
        private static bool _secondary;

        private static bool _moderation;

        extension(PlayerInspectorTool tool)
        {
            public bool ShowSecondaryInventory
            {
                get => _secondary;
                set => _secondary = value;
            }

            public bool ShowModerationTools
            {
                get => _moderation;
                set => _moderation = value;
            }
        }
    }
}
