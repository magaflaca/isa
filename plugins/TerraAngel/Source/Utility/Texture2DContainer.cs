using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
// Port: Color is a global alias already.

namespace TerraAngel.Utility;

public struct Texture2DContainer : IDisposable
{
    public IntPtr TextureBindId { get; private set; } = IntPtr.Zero;
    public Texture2D? Texture { get; private set; } = null;

    public bool IsActive => TextureBindId != IntPtr.Zero && Texture != null;

    public Texture2DContainer()
    {
    }

    public void Dispose()
    {
        Unload();
    }

    public void Load(string imageFilePath, Action<Image<Rgba32>>? preprocessingOperation = null)
    {
        using var image = Image.Load<Rgba32>(imageFilePath);
        preprocessingOperation?.Invoke(image);
        Load(image); // call image loading overload
    }

    public void Load(Image<Rgba32> image)
    {
        // Port: the pixels are already RGBA (the packing of XNA's Color): no pooled buffer, no row spans.
        var textureData = new Color[image.Width * image.Height];
        for (int i = 0; i < textureData.Length; i++)
        {
            textureData[i] = new Color { PackedValue = image.Pixels[i] };
        }
        Load(textureData, image.Width, image.Height);
    }

    public void Load(Color[] textureData, int width, int height)
    {
        // sanity check
        if (width * height > textureData.Length)
        {
            throw new ArgumentOutOfRangeException($"{nameof(width)} * {nameof(height)}");
        }

        // place into variables first to avoid partial bind
        var texture = new Texture2D(Main.graphics.GraphicsDevice, width, height);
        var textureBindId = IntPtr.Zero;

        try
        {
            // Port: XNA's SetData (FNA's SetDataPointerEXT is not there).
            texture.SetData(textureData, 0, width * height);

            // bind it!
            textureBindId = ClientLoader.MainRenderer?.BindTexture(texture) ?? throw new Exception("Failed to bind texture");
        }
        catch (Exception)
        {
            // we are fucked up...
            texture.Dispose();
            if (textureBindId != IntPtr.Zero)
                ClientLoader.MainRenderer?.UnbindTexture(textureBindId);
            throw;
        }

        // unload here
        Unload();

        // bind to container
        Texture = texture;
        TextureBindId = textureBindId;
    }

    public void Unload()
    {
        if (TextureBindId != IntPtr.Zero)
            ClientLoader.MainRenderer?.UnbindTexture(TextureBindId);
        TextureBindId = IntPtr.Zero;

        Texture?.Dispose();
        Texture = null;
    }
}