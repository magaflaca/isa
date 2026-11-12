namespace TerraAngel;

/// <summary>
/// TerraAngel's Main.ApplyViewRangeLimits. In the original game the world view size is a constant (built into the code
/// that uses it), so only the window size limits can change here; the larger Rod of Discord / Zenith ranges need patches.
/// </summary>
public static class ViewRangeLimits
{
    public static void Apply(bool enabled)
    {
        Main.maxScreenW = enabled ? 6480 : 1920;
        Main.maxScreenH = enabled ? 4320 : 1200;
    }
}
