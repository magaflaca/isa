using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraAngel.Tools.Map;

// Port: TerraAngel copied the image with SDL's window handle and Span2D; here it is a System.Drawing bitmap saved as a
// PNG (Screenshots folder) and copied with Windows Forms' clipboard. Taking one starts from the tool's buttons too (the
// key is unbound by default), the map does not move while selecting, and the size is limited so a 32-bit game can make it.
public class MapScreenshotTool : Tool
{
    public override string Name => GetString("Map Screenshot");

    public override ToolTabs Tab => ToolTabs.MiscTools;

    public bool TakingScreenshot = false;

    public bool SelectingArea = false;

    public Vector2i ScreenshotOrigin = new Vector2i(0, 0);

    public ref int PixelsPerTile => ref ClientConfig.Settings.MapScreenshotPixelsPerTile;

    /// <summary>Largest image made (pixels); bigger areas get fewer pixels per tile.</summary>
    private const long MaxPixels = 64L * 1024 * 1024;

    /// <summary>Largest image also put in the clipboard (a copy of it is made there).</summary>
    private const long MaxClipboardPixels = 16L * 1024 * 1024;

    private bool _working;

    private string _status = "";

    public static string Folder => Path.Combine(ClientLoader.SavePath, "Screenshots");

    public override void DrawUI(ImGuiIOPtr io)
    {
        if (!ImGui.CollapsingHeader(GetString("Map Screenshot")))
        {
            return;
        }
        ImGui.SliderInt(GetString("Map Pixel per Tile"), ref PixelsPerTile, 1, 32);
        PixelsPerTile = Math.Max(1, PixelsPerTile);
        ImGui.BeginDisabled(Main.gameMenu || _working);
        if (ImGui.Button(GetString("Select area on the map")))
        {
            StartSelecting();
        }
        ImGui.SameLine();
        if (ImGui.Button(GetString("Whole world")))
        {
            TakeMapScreenshot(new Vector2i(0, 0), new Vector2i(Main.maxTilesX, Main.maxTilesY));
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(GetString("Open folder")))
        {
            Directory.CreateDirectory(Folder);
            System.Diagnostics.Process.Start(Folder);
        }
        if (_status.Length > 0)
        {
            ImGui.TextWrapped(_status);
        }
    }

    private void StartSelecting()
    {
        TakingScreenshot = true;
        SelectingArea = false;
        if (!Main.mapFullscreen)
        {
            Main.mapFullscreen = true;
            Main.resetMapFull = true;
            Main.playerInventory = false;
        }
    }

    public override void Update()
    {
        if (Main.gameMenu || !Main.mapFullscreen)
        {
            TakingScreenshot = false;
            SelectingArea = false;
            return;
        }

        if (InputSystem.IsKeyPressed(ClientConfig.Settings.TakeMapScreenshot))
        {
            StartSelecting();
        }

        if (!TakingScreenshot)
        {
            return;
        }

        // The map would be dragged by the same mouse button.
        IsaClient.Plugins.PluginHost.CaptureGameMouse = true;
        ImDrawListPtr drawList = ImGui.GetBackgroundDrawList();
        Vector2 mouse = InputSystem.MousePosition;
        Vector2i mouseTile = ClampToWorld(Util.ScreenToWorldFullscreenMap(mouse) / 16f);

        if (InputSystem.RightMousePressed || InputSystem.IsKeyPressedRaw(Microsoft.Xna.Framework.Input.Keys.Escape))
        {
            TakingScreenshot = false;
            SelectingArea = false;
            return;
        }

        if (!SelectingArea)
        {
            drawList.AddText(mouse + new Vector2(16f, 16f), Color.White.PackedValue, GetString("Drag over the area to capture (right click cancels)"));
        }

        if (InputSystem.LeftMousePressed)
        {
            SelectingArea = true;
            ScreenshotOrigin = mouseTile;
        }

        if (SelectingArea)
        {
            drawList.DrawTileRect(new Vector2(ScreenshotOrigin.X, ScreenshotOrigin.Y), new Vector2(mouseTile.X, mouseTile.Y), new Color(1f, 0f, 0f, 0.7f).PackedValue);
            drawList.AddText(mouse + new Vector2(16f, 16f), Color.White.PackedValue, $"{Math.Abs(mouseTile.X - ScreenshotOrigin.X)} x {Math.Abs(mouseTile.Y - ScreenshotOrigin.Y)}");

            if (InputSystem.LeftMouseReleased)
            {
                TakeMapScreenshot(ScreenshotOrigin, mouseTile);
                TakingScreenshot = false;
                SelectingArea = false;
            }
        }
    }

    private static Vector2i ClampToWorld(Vector2 tile)
    {
        return new Vector2i(Math.Max(0, Math.Min(Main.maxTilesX, (int)tile.X)), Math.Max(0, Math.Min(Main.maxTilesY, (int)tile.Y)));
    }

    public void TakeMapScreenshot(Vector2i start, Vector2i end)
    {
        if (_working || Main.Map == null)
        {
            return;
        }
        int left = Math.Max(0, Math.Min(start.X, end.X));
        int top = Math.Max(0, Math.Min(start.Y, end.Y));
        int right = Math.Min(Main.maxTilesX, Math.Max(start.X, end.X));
        int bottom = Math.Min(Main.maxTilesY, Math.Max(start.Y, end.Y));
        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int ppt = Math.Max(1, PixelsPerTile);
        while (ppt > 1 && (long)width * height * ppt * ppt > MaxPixels)
        {
            ppt--;
        }
        if ((long)width * height > MaxPixels)
        {
            _status = GetString("That area is too big.");
            return;
        }

        string file = Path.Combine(Folder, $"map-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
        _working = true;
        _status = GetString("Taking map screenshot...");
        ClientLoader.Console.WriteLine(_status);

        Task.Run(() =>
        {
            try
            {
                int imageWidth = width * ppt;
                int imageHeight = height * ppt;
                int[] row = new int[imageWidth];
                using System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(imageWidth, imageHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                System.Drawing.Imaging.BitmapData data = bitmap.LockBits(new NRectangle(0, 0, imageWidth, imageHeight), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                try
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            Color col = Terraria.Map.MapHelper.GetMapTileXnaColor(Main.Map[x + left, y + top], x + left, y + top);
                            int bgra = (255 << 24) | (col.R << 16) | (col.G << 8) | col.B;
                            for (int xp = 0; xp < ppt; xp++)
                            {
                                row[x * ppt + xp] = bgra;
                            }
                        }
                        for (int yp = 0; yp < ppt; yp++)
                        {
                            Marshal.Copy(row, 0, data.Scan0 + (y * ppt + yp) * data.Stride, imageWidth);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                Directory.CreateDirectory(Folder);
                bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                bool copied = (long)imageWidth * imageHeight <= MaxClipboardPixels && CopyToClipboard(bitmap);
                _status = GetString($"Saved {imageWidth}x{imageHeight} ({ppt} px per tile) to {Path.GetFileName(file)}") + (copied ? GetString(" and copied it.") : ".");
                ClientLoader.Console.WriteLine(_status);
            }
            catch (Exception ex)
            {
                _status = GetString("The map screenshot failed: ") + ex.Message;
                ClientLoader.Console.WriteError(_status);
            }
            finally
            {
                _working = false;
            }
        });
    }

    /// <summary>The clipboard needs a single-threaded apartment thread.</summary>
    private static bool CopyToClipboard(System.Drawing.Bitmap bitmap)
    {
        bool copied = false;
        Thread thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetImage(bitmap);
                copied = true;
            }
            catch
            {
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);
        return copied;
    }
}
