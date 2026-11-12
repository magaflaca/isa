using System;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace TerraAngel.Compat;

// ImGui calls that take a vector by ref (DragFloat2, ColorEdit3, SliderFloat4...) also take the bridge vectors: in
// TerraAngel code they are the "Vector2/3/4" a ref points to. Generated from ImGui.NET 1.91.0.1 (C# 14 extension members).
public static class ImGuiBridgeOverloads
{
	extension(ImGui)
	{
		public static bool ColorEdit3(string label, ref Vector3 col)
		{
			NVector3 colN = col;
			bool result = ImGui.ColorEdit3(label, ref colN);
			col = colN;
			return result;
		}

		public static bool ColorEdit3(string label, ref Vector3 col, ImGuiNET.ImGuiColorEditFlags flags)
		{
			NVector3 colN = col;
			bool result = ImGui.ColorEdit3(label, ref colN, flags);
			col = colN;
			return result;
		}

		public static bool ColorEdit4(string label, ref Vector4 col)
		{
			NVector4 colN = col;
			bool result = ImGui.ColorEdit4(label, ref colN);
			col = colN;
			return result;
		}

		public static bool ColorEdit4(string label, ref Vector4 col, ImGuiNET.ImGuiColorEditFlags flags)
		{
			NVector4 colN = col;
			bool result = ImGui.ColorEdit4(label, ref colN, flags);
			col = colN;
			return result;
		}

		public static bool ColorPicker3(string label, ref Vector3 col, ImGuiNET.ImGuiColorEditFlags flags)
		{
			NVector3 colN = col;
			bool result = ImGui.ColorPicker3(label, ref colN, flags);
			col = colN;
			return result;
		}

		public static bool ColorPicker3(string label, ref Vector3 col)
		{
			NVector3 colN = col;
			bool result = ImGui.ColorPicker3(label, ref colN);
			col = colN;
			return result;
		}

		public static bool ColorPicker4(string label, ref Vector4 col)
		{
			NVector4 colN = col;
			bool result = ImGui.ColorPicker4(label, ref colN);
			col = colN;
			return result;
		}

		public static bool ColorPicker4(string label, ref Vector4 col, ImGuiNET.ImGuiColorEditFlags flags)
		{
			NVector4 colN = col;
			bool result = ImGui.ColorPicker4(label, ref colN, flags);
			col = colN;
			return result;
		}

		public static bool ColorPicker4(string label, ref Vector4 col, ImGuiNET.ImGuiColorEditFlags flags, ref float ref_col)
		{
			NVector4 colN = col;
			bool result = ImGui.ColorPicker4(label, ref colN, flags, ref ref_col);
			col = colN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN);
			v = vN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v, float v_speed)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN, v_speed);
			v = vN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v, float v_speed, float v_min)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN, v_speed, v_min);
			v = vN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v, float v_speed, float v_min, float v_max)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN, v_speed, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v, float v_speed, float v_min, float v_max, string format)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN, v_speed, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool DragFloat2(string label, ref Vector2 v, float v_speed, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector2 vN = v;
			bool result = ImGui.DragFloat2(label, ref vN, v_speed, v_min, v_max, format, flags);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v, float v_speed)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN, v_speed);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v, float v_speed, float v_min)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN, v_speed, v_min);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v, float v_speed, float v_min, float v_max)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN, v_speed, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v, float v_speed, float v_min, float v_max, string format)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN, v_speed, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool DragFloat3(string label, ref Vector3 v, float v_speed, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector3 vN = v;
			bool result = ImGui.DragFloat3(label, ref vN, v_speed, v_min, v_max, format, flags);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v, float v_speed)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN, v_speed);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v, float v_speed, float v_min)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN, v_speed, v_min);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v, float v_speed, float v_min, float v_max)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN, v_speed, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v, float v_speed, float v_min, float v_max, string format)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN, v_speed, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool DragFloat4(string label, ref Vector4 v, float v_speed, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector4 vN = v;
			bool result = ImGui.DragFloat4(label, ref vN, v_speed, v_min, v_max, format, flags);
			v = vN;
			return result;
		}

		public static bool InputFloat2(string label, ref Vector2 v)
		{
			NVector2 vN = v;
			bool result = ImGui.InputFloat2(label, ref vN);
			v = vN;
			return result;
		}

		public static bool InputFloat2(string label, ref Vector2 v, string format)
		{
			NVector2 vN = v;
			bool result = ImGui.InputFloat2(label, ref vN, format);
			v = vN;
			return result;
		}

		public static bool InputFloat2(string label, ref Vector2 v, string format, ImGuiNET.ImGuiInputTextFlags flags)
		{
			NVector2 vN = v;
			bool result = ImGui.InputFloat2(label, ref vN, format, flags);
			v = vN;
			return result;
		}

		public static bool InputFloat3(string label, ref Vector3 v)
		{
			NVector3 vN = v;
			bool result = ImGui.InputFloat3(label, ref vN);
			v = vN;
			return result;
		}

		public static bool InputFloat3(string label, ref Vector3 v, string format)
		{
			NVector3 vN = v;
			bool result = ImGui.InputFloat3(label, ref vN, format);
			v = vN;
			return result;
		}

		public static bool InputFloat3(string label, ref Vector3 v, string format, ImGuiNET.ImGuiInputTextFlags flags)
		{
			NVector3 vN = v;
			bool result = ImGui.InputFloat3(label, ref vN, format, flags);
			v = vN;
			return result;
		}

		public static bool InputFloat4(string label, ref Vector4 v)
		{
			NVector4 vN = v;
			bool result = ImGui.InputFloat4(label, ref vN);
			v = vN;
			return result;
		}

		public static bool InputFloat4(string label, ref Vector4 v, string format)
		{
			NVector4 vN = v;
			bool result = ImGui.InputFloat4(label, ref vN, format);
			v = vN;
			return result;
		}

		public static bool InputFloat4(string label, ref Vector4 v, string format, ImGuiNET.ImGuiInputTextFlags flags)
		{
			NVector4 vN = v;
			bool result = ImGui.InputFloat4(label, ref vN, format, flags);
			v = vN;
			return result;
		}

		public static bool IsMousePosValid(ref Vector2 mouse_pos)
		{
			NVector2 mouse_posN = mouse_pos;
			bool result = ImGui.IsMousePosValid(ref mouse_posN);
			mouse_pos = mouse_posN;
			return result;
		}

		public static bool SliderFloat2(string label, ref Vector2 v, float v_min, float v_max)
		{
			NVector2 vN = v;
			bool result = ImGui.SliderFloat2(label, ref vN, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool SliderFloat2(string label, ref Vector2 v, float v_min, float v_max, string format)
		{
			NVector2 vN = v;
			bool result = ImGui.SliderFloat2(label, ref vN, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool SliderFloat2(string label, ref Vector2 v, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector2 vN = v;
			bool result = ImGui.SliderFloat2(label, ref vN, v_min, v_max, format, flags);
			v = vN;
			return result;
		}

		public static bool SliderFloat3(string label, ref Vector3 v, float v_min, float v_max)
		{
			NVector3 vN = v;
			bool result = ImGui.SliderFloat3(label, ref vN, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool SliderFloat3(string label, ref Vector3 v, float v_min, float v_max, string format)
		{
			NVector3 vN = v;
			bool result = ImGui.SliderFloat3(label, ref vN, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool SliderFloat3(string label, ref Vector3 v, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector3 vN = v;
			bool result = ImGui.SliderFloat3(label, ref vN, v_min, v_max, format, flags);
			v = vN;
			return result;
		}

		public static bool SliderFloat4(string label, ref Vector4 v, float v_min, float v_max)
		{
			NVector4 vN = v;
			bool result = ImGui.SliderFloat4(label, ref vN, v_min, v_max);
			v = vN;
			return result;
		}

		public static bool SliderFloat4(string label, ref Vector4 v, float v_min, float v_max, string format)
		{
			NVector4 vN = v;
			bool result = ImGui.SliderFloat4(label, ref vN, v_min, v_max, format);
			v = vN;
			return result;
		}

		public static bool SliderFloat4(string label, ref Vector4 v, float v_min, float v_max, string format, ImGuiNET.ImGuiSliderFlags flags)
		{
			NVector4 vN = v;
			bool result = ImGui.SliderFloat4(label, ref vN, v_min, v_max, format, flags);
			v = vN;
			return result;
		}
	}
}
