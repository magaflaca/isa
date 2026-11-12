namespace TerraAngel;

/// <summary>TerraAngel's translations (GetText). IsaClient is in English: the texts are used as they are.</summary>
public static class I18n
{
    public static string GetString(string text) => text;

    public static string GetString(FormattableString text) => text?.ToString() ?? "";

    public static string GetParticularString(string context, string text) => text;
}
