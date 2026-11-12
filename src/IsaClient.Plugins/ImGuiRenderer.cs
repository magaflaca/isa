using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NVector2 = System.Numerics.Vector2;

namespace IsaClient.Plugins;

/// <summary>
/// Draws ImGui with XNA (Reach profile): the font atlas and the textures plugins bind become Texture2D, every draw list
/// is copied into one dynamic vertex/index buffer and drawn with a BasicEffect, clipped with the scissor rectangle.
/// Based on TerraAngel's ImGuiRenderer (MIT, CEO-Chair), moved from FNA to XNA 4.
/// </summary>
public sealed class ImGuiRenderer
{
	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct Vertex : IVertexType
	{
		public static readonly VertexDeclaration Declaration = new VertexDeclaration(20,
			new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
			new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
			new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

		public Vector2 Position;

		public Vector2 Uv;

		public uint Color;

		VertexDeclaration IVertexType.VertexDeclaration => Declaration;
	}

	private readonly GraphicsDevice _device;

	private readonly BasicEffect _effect;

	private readonly RasterizerState _rasterizer = new RasterizerState
	{
		CullMode = CullMode.None,
		ScissorTestEnable = true,
		FillMode = FillMode.Solid
	};

	private readonly Dictionary<IntPtr, Texture2D> _textures = new Dictionary<IntPtr, Texture2D>();

	private int _nextTextureId = 1;

	private IntPtr? _fontTextureId;

	private DynamicVertexBuffer _vertexBuffer;

	private DynamicIndexBuffer _indexBuffer;

	private Vertex[] _vertices = new Vertex[0];

	private ushort[] _indices = new ushort[0];

	private IntPtr _iniPath;

	public ImGuiRenderer(GraphicsDevice device, string iniPath)
	{
		_device = device;
		ImGui.SetCurrentContext(ImGui.CreateContext());
		ImGuiIOPtr io = ImGui.GetIO();
		SetIniFile(io, iniPath);
		io.BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
		_effect = new BasicEffect(device)
		{
			TextureEnabled = true,
			VertexColorEnabled = true,
			World = Matrix.Identity,
			View = Matrix.Identity
		};
		LoadFonts(io);
		ImGuiTheme.Apply();
	}

	private unsafe void SetIniFile(ImGuiIOPtr io, string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			io.NativePtr->IniFilename = null;
			return;
		}
		// ImGui keeps the pointer: the string lives as long as the renderer.
		_iniPath = Marshal.StringToHGlobalAnsi(path);
		io.NativePtr->IniFilename = (byte*)_iniPath;
	}

	/// <summary>Segoe UI when Windows has it (easier to read than ImGui's pixel font), ImGui's own font otherwise.</summary>
	private unsafe void LoadFonts(ImGuiIOPtr io)
	{
		string segoe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
		bool loaded = false;
		if (File.Exists(segoe))
		{
			try
			{
				loaded = io.Fonts.AddFontFromFileTTF(segoe, 17f).NativePtr != null;
			}
			catch
			{
			}
		}
		if (!loaded)
		{
			io.Fonts.AddFontDefault();
		}
		RebuildFontAtlas();
	}

	public void RebuildFontAtlas()
	{
		ImGuiIOPtr io = ImGui.GetIO();
		// XNA's Reach profile allows textures up to 2048 px: a wide atlas keeps the height down.
		io.Fonts.TexDesiredWidth = 2048;
		io.Fonts.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);
		if (width > 2048 || height > 2048)
		{
			throw new InvalidOperationException("the ImGui font atlas is " + width + "x" + height + ", more than the 2048 px the game allows (too many or too big fonts)");
		}
		byte[] data = new byte[width * height * bytesPerPixel];
		Marshal.Copy(pixels, data, 0, data.Length);
		Texture2D texture = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
		texture.SetData(data);
		if (_fontTextureId.HasValue)
		{
			UnbindTexture(_fontTextureId.Value);
		}
		_fontTextureId = BindTexture(texture);
		io.Fonts.SetTexID(_fontTextureId.Value);
		io.Fonts.ClearTexData();
	}

	/// <summary>An id for <paramref name="texture"/> to use in ImGui.Image and draw lists.</summary>
	public IntPtr BindTexture(Texture2D texture)
	{
		IntPtr id = new IntPtr(_nextTextureId++);
		_textures[id] = texture;
		return id;
	}

	public void UnbindTexture(IntPtr id)
	{
		_textures.Remove(id);
	}

	public void BeginFrame(float deltaSeconds)
	{
		ImGuiIOPtr io = ImGui.GetIO();
		PresentationParameters pp = _device.PresentationParameters;
		io.DisplaySize = new NVector2(pp.BackBufferWidth, pp.BackBufferHeight);
		io.DisplayFramebufferScale = NVector2.One;
		io.DeltaTime = Math.Max(deltaSeconds, 1f / 1000f);
		ImGui.NewFrame();
	}

	// ImGui.NET 1.91.0.1 on purpose: from imgui 1.91.4 ImTextureID is 64-bit while ImGui.NET still binds it as IntPtr,
	// which on x86 shifts every draw command (nothing is drawn) and every argument after a texture id.
	// Its released cimgui.dll keeps its asserts: a plugin that forgets an End() (or a PopID...) would stop the game with
	// an assert dialog at the end of the frame, so whatever is left open is closed first.
	[DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
	private static extern void igErrorCheckEndFrameRecover(IntPtr logCallback, IntPtr userData);

	/// <summary>Closes the windows, IDs, styles... a plugin left open (a missing End()), so the frame can end.</summary>
	public static void RecoverFromErrors()
	{
		igErrorCheckEndFrameRecover(IntPtr.Zero, IntPtr.Zero);
	}

	public void EndFrame()
	{
		RecoverFromErrors();
		ImGui.Render();
		Render(ImGui.GetDrawData());
	}

	private void Render(ImDrawDataPtr drawData)
	{
		if (drawData.CmdListsCount == 0 || drawData.TotalVtxCount == 0)
		{
			return;
		}
		Rectangle oldScissor = _device.ScissorRectangle;
		BlendState oldBlend = _device.BlendState;
		RasterizerState oldRasterizer = _device.RasterizerState;
		DepthStencilState oldDepth = _device.DepthStencilState;
		SamplerState oldSampler = _device.SamplerStates[0];
		try
		{
			_device.BlendState = BlendState.NonPremultiplied;
			_device.RasterizerState = _rasterizer;
			_device.DepthStencilState = DepthStencilState.None;
			_device.SamplerStates[0] = SamplerState.LinearClamp;
			UploadBuffers(drawData);
			DrawCommands(drawData);
		}
		finally
		{
			_device.ScissorRectangle = oldScissor;
			_device.BlendState = oldBlend;
			_device.RasterizerState = oldRasterizer;
			_device.DepthStencilState = oldDepth;
			_device.SamplerStates[0] = oldSampler;
			_device.SetVertexBuffer(null);
			_device.Indices = null;
		}
	}

	private unsafe void UploadBuffers(ImDrawDataPtr drawData)
	{
		int vertexCount = drawData.TotalVtxCount;
		int indexCount = drawData.TotalIdxCount;
		if (_vertexBuffer == null || _vertexBuffer.VertexCount < vertexCount || _vertexBuffer.IsContentLost)
		{
			_vertexBuffer?.Dispose();
			int size = Math.Max(vertexCount * 3 / 2, 4096);
			_vertexBuffer = new DynamicVertexBuffer(_device, Vertex.Declaration, size, BufferUsage.WriteOnly);
			_vertices = new Vertex[size];
		}
		if (_indexBuffer == null || _indexBuffer.IndexCount < indexCount || _indexBuffer.IsContentLost)
		{
			_indexBuffer?.Dispose();
			int size = Math.Max(indexCount * 3 / 2, 8192);
			_indexBuffer = new DynamicIndexBuffer(_device, IndexElementSize.SixteenBits, size, BufferUsage.WriteOnly);
			_indices = new ushort[size];
		}
		int vertexOffset = 0;
		int indexOffset = 0;
		fixed (Vertex* vertices = _vertices)
		fixed (ushort* indices = _indices)
		{
			for (int i = 0; i < drawData.CmdListsCount; i++)
			{
				ImDrawListPtr list = drawData.CmdLists[i];
				long vertexBytes = list.VtxBuffer.Size * sizeof(Vertex);
				long indexBytes = list.IdxBuffer.Size * sizeof(ushort);
				Buffer.MemoryCopy((void*)list.VtxBuffer.Data, vertices + vertexOffset, (_vertices.Length - vertexOffset) * (long)sizeof(Vertex), vertexBytes);
				Buffer.MemoryCopy((void*)list.IdxBuffer.Data, indices + indexOffset, (_indices.Length - indexOffset) * (long)sizeof(ushort), indexBytes);
				vertexOffset += list.VtxBuffer.Size;
				indexOffset += list.IdxBuffer.Size;
			}
		}
		_vertexBuffer.SetData(_vertices, 0, vertexCount, SetDataOptions.Discard);
		_indexBuffer.SetData(_indices, 0, indexCount, SetDataOptions.Discard);
	}

	private void DrawCommands(ImDrawDataPtr drawData)
	{
		_device.SetVertexBuffer(_vertexBuffer);
		_device.Indices = _indexBuffer;
		Viewport viewport = _device.Viewport;
		// Half a pixel back: Direct3D 9 samples texel centers at whole pixels.
		_effect.Projection = Matrix.CreateOrthographicOffCenter(0.5f, viewport.Width + 0.5f, viewport.Height + 0.5f, 0.5f, -1f, 1f);
		Rectangle bounds = new Rectangle(0, 0, viewport.Width, viewport.Height);
		int vertexBase = 0;
		int indexBase = 0;
		for (int i = 0; i < drawData.CmdListsCount; i++)
		{
			ImDrawListPtr list = drawData.CmdLists[i];
			for (int c = 0; c < list.CmdBuffer.Size; c++)
			{
				ImDrawCmdPtr cmd = list.CmdBuffer[c];
				if (cmd.UserCallback != IntPtr.Zero || cmd.ElemCount == 0)
				{
					continue;
				}
				System.Numerics.Vector4 clip = cmd.ClipRect;
				Rectangle scissor = Rectangle.Intersect(bounds, new Rectangle((int)clip.X, (int)clip.Y, (int)(clip.Z - clip.X), (int)(clip.W - clip.Y)));
				if (scissor.Width <= 0 || scissor.Height <= 0)
				{
					continue;
				}
				if (!_textures.TryGetValue(cmd.TextureId, out Texture2D texture) || texture == null || texture.IsDisposed)
				{
					continue;
				}
				_device.ScissorRectangle = scissor;
				_effect.Texture = texture;
				foreach (EffectPass pass in _effect.CurrentTechnique.Passes)
				{
					pass.Apply();
					_device.DrawIndexedPrimitives(PrimitiveType.TriangleList, vertexBase + (int)cmd.VtxOffset, 0, list.VtxBuffer.Size, indexBase + (int)cmd.IdxOffset, (int)cmd.ElemCount / 3);
				}
			}
			vertexBase += list.VtxBuffer.Size;
			indexBase += list.IdxBuffer.Size;
		}
	}
}
