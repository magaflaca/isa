using System;
using System.Linq;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.GameContent.UI;
using Terraria.UI.Chat;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace IsaClient.Plugins;

/// <summary>
/// ImGui helpers for plugins (colored text, color pickers, help markers, Terraria items as images, after TerraAngel's
/// ImGuiUtil) and the conversions between XNA's vectors and the System.Numerics ones ImGui uses (XNA has no implicit
/// conversion). TerraAngel's full ImGuiUtil comes with the TerraAngel pack.
/// </summary>
public static class ImGuiExt
{
	private static readonly IntPtr[] _itemImages = new IntPtr[Terraria.ID.ItemID.Count];

	public static NVector2 ToNumerics(this Vector2 v) => new NVector2(v.X, v.Y);

	public static NVector3 ToNumerics(this Vector3 v) => new NVector3(v.X, v.Y, v.Z);

	public static NVector4 ToNumerics(this Vector4 v) => new NVector4(v.X, v.Y, v.Z, v.W);

	public static Vector2 ToXna(this NVector2 v) => new Vector2(v.X, v.Y);

	public static Vector3 ToXna(this NVector3 v) => new Vector3(v.X, v.Y, v.Z);

	public static Vector4 ToXna(this NVector4 v) => new Vector4(v.X, v.Y, v.Z, v.W);

	/// <summary>A color as ImGui wants it in draw lists (0xAABBGGRR, the same as Color.PackedValue).</summary>
	public static uint ToImGui(this Color color) => color.PackedValue;

	public static Color WithAlpha(this Color color, float alpha) => new Color(color.R, color.G, color.B, (byte)MathHelper.Clamp(alpha * 255f, 0f, 255f));

	public static void TextColored(string text, Color color)
	{
		ImGui.PushStyleColor(ImGuiCol.Text, color.PackedValue);
		ImGui.TextUnformatted(text);
		ImGui.PopStyleColor();
	}

	public static void TextColored(string text, uint color)
	{
		ImGui.PushStyleColor(ImGuiCol.Text, color);
		ImGui.TextUnformatted(text);
		ImGui.PopStyleColor();
	}

	public static bool ColorEdit3(string label, ref Color color)
	{
		NVector3 v = color.ToVector3().ToNumerics();
		if (ImGui.ColorEdit3(label, ref v))
		{
			color = new Color(v.X, v.Y, v.Z, color.A / 255f);
			return true;
		}
		return false;
	}

	public static bool ColorEdit4(string label, ref Color color)
	{
		NVector4 v = color.ToVector4().ToNumerics();
		if (ImGui.ColorEdit4(label, ref v))
		{
			color = new Color(v.X, v.Y, v.Z, v.W);
			return true;
		}
		return false;
	}

	/// <summary>A gray (?) that shows <paramref name="desc"/> when hovered.</summary>
	public static void HelpMarker(string desc)
	{
		ImGui.TextDisabled("(?)");
		if (ImGui.BeginItemTooltip())
		{
			ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
			ImGui.TextUnformatted(desc);
			ImGui.PopTextWrapPos();
			ImGui.EndTooltip();
		}
	}

	public static void HelpMarkerTopRight(string desc)
	{
		NVector2 size = ImGui.CalcTextSize("(?)");
		NVector2 cursor = ImGui.GetCursorScreenPos();
		NVector2 avail = ImGui.GetContentRegionAvail();
		ImGui.SetCursorScreenPos(new NVector2(cursor.X + avail.X - size.X - ImGui.GetStyle().FramePadding.X, cursor.Y));
		HelpMarker(desc);
		ImGui.SetCursorScreenPos(cursor);
	}

	/// <summary>The ImGui texture of an item's sprite (loaded the first time).</summary>
	public static IntPtr ItemImage(int id)
	{
		if (id <= 0 || id >= _itemImages.Length)
		{
			return IntPtr.Zero;
		}
		if (_itemImages[id] == IntPtr.Zero)
		{
			Main.instance.LoadItem(id);
			_itemImages[id] = TerraAngel.ClientLoader.MainRenderer.BindTexture(TextureAssets.Item[id].Value);
		}
		return _itemImages[id];
	}

	public static void DrawItem(this ImDrawListPtr drawList, int id, NVector2 position, NVector2 size, float alpha = 1f)
	{
		IntPtr image = ItemImage(id);
		if (image == IntPtr.Zero)
		{
			return;
		}
		Texture2D texture = TextureAssets.Item[id].Value;
		Rectangle frame = (Main.itemAnimations[id] != null) ? Main.itemAnimations[id].GetFrame(texture) : texture.Frame();
		float scale = Math.Min(1f, Math.Min(size.X / frame.Width, size.Y / frame.Height));
		NVector2 drawn = new NVector2(frame.Width * scale, frame.Height * scale);
		NVector2 start = position + size / 2f - drawn / 2f;
		start = new NVector2((float)Math.Floor(start.X), (float)Math.Floor(start.Y));
		NVector2 uvMin = new NVector2((float)frame.X / texture.Width, (float)frame.Y / texture.Height);
		NVector2 uvMax = new NVector2((float)frame.Right / texture.Width, (float)frame.Bottom / texture.Height);
		drawList.AddImage(image, start, start + drawn, uvMin, uvMax, Color.White.WithAlpha(alpha).PackedValue);
	}

	public static bool ItemButton(int id, string uid, bool showTooltip = true, bool isSelected = false, float margin = 6f, float alpha = 1f)
	{
		return ItemButton(id, uid, new NVector2(32f, 32f), showTooltip, isSelected, margin, alpha);
	}

	public static bool ItemButton(int id, string uid, NVector2 size, bool showTooltip = true, bool isSelected = false, float margin = 6f, float alpha = 1f)
	{
		ImGui.PushID(uid);
		bool clicked = ImGui.Button("", size + new NVector2(margin));
		NVector2 min = ImGui.GetItemRectMin();
		NVector2 max = ImGui.GetItemRectMax();
		ImGui.PopID();
		if (ImGui.IsRectVisible(min, max))
		{
			ImDrawListPtr drawList = ImGui.GetWindowDrawList();
			if (isSelected)
			{
				drawList.AddRect(min, max, 0xFF45E7FFu, 4f, ImDrawFlags.None, 2f);
			}
			drawList.DrawItem(id, min + new NVector2(margin / 2f), size, alpha);
			if (showTooltip && ImGui.IsMouseHoveringRect(min, max) && ContentSamples.ItemsByType.TryGetValue(id, out Item sample))
			{
				try
				{
					ItemTooltip(sample);
				}
				catch
				{
				}
			}
		}
		return clicked;
	}

	/// <summary>The game's tooltip of <paramref name="item"/> as an ImGui tooltip.</summary>
	public static void ItemTooltip(Item item)
	{
		int yoyoLogo = 0;
		int lines = 1;
		string[] text = new string[30];
		Color[] colors = new Color[30];
		Main.MouseText_DrawItemTooltip_GetLinesInfo(item, ref yoyoLogo, item.knockBack, ref lines, text, colors);
		ImGui.BeginTooltip();
		for (int i = 0; i < lines; i++)
		{
			Color color = (i == 0) ? ItemRarity.GetColor(item.rare) : ((colors[i] == default(Color)) ? Color.White : colors[i]);
			TextColored(ChatManager.ParseMessage(text[i] ?? "", color).Aggregate("", (s, snippet) => s + snippet.Text), color);
		}
		ImGui.EndTooltip();
	}

}
