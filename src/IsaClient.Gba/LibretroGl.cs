using System;
using System.Runtime.InteropServices;
using System.Text;

namespace IsaClient.Gba;

/// <summary>
/// The graphics card for cores that draw with it (the PSP's): an OpenGL context on a hidden window of its own, apart from
/// Terraria's Direct3D, with a framebuffer the core draws into. Each picture is read back (a frame late, so nothing
/// waits for the card) and goes on like any other core's, ready for an XNA texture. <see cref="Prepare"/> runs on the
/// main thread (which owns the hidden window); the rest on the machine's thread. The context lives as long as the game
/// process: drivers don't always like a core's cached functions outliving it.
/// </summary>
internal static class LibretroGl
{
	#region Native

	[StructLayout(LayoutKind.Sequential)]
	private struct PixelFormatDescriptor
	{
		public ushort Size;
		public ushort Version;
		public uint Flags;
		public byte PixelType;
		public byte ColorBits;
		public byte RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift;
		public byte AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits;
		public byte DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
		public uint LayerMask, VisibleMask, DamageMask;
	}

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

	[DllImport("user32")]
	private static extern IntPtr GetDC(IntPtr window);

	[DllImport("gdi32", SetLastError = true)]
	private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormatDescriptor format);

	[DllImport("gdi32", SetLastError = true)]
	private static extern bool SetPixelFormat(IntPtr dc, int index, ref PixelFormatDescriptor format);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	private static extern IntPtr GetModuleHandle(string name);

	[DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	[DllImport("opengl32", SetLastError = true)]
	private static extern IntPtr wglCreateContext(IntPtr dc);

	[DllImport("opengl32", SetLastError = true)]
	private static extern bool wglDeleteContext(IntPtr context);

	[DllImport("opengl32", SetLastError = true)]
	private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);

	[DllImport("opengl32", CharSet = CharSet.Ansi)]
	private static extern IntPtr wglGetProcAddress(string name);

	[DllImport("opengl32")]
	private static extern IntPtr glGetString(uint name);

	[DllImport("opengl32")]
	private static extern void glGetIntegerv(uint name, out int value);

	[DllImport("opengl32")]
	private static extern void glGenTextures(int count, out uint texture);

	[DllImport("opengl32")]
	private static extern void glDeleteTextures(int count, ref uint texture);

	[DllImport("opengl32")]
	private static extern void glBindTexture(uint target, uint texture);

	[DllImport("opengl32")]
	private static extern void glTexParameteri(uint target, uint name, int value);

	[DllImport("opengl32")]
	private static extern void glTexImage2D(uint target, int level, int inner, int width, int height, int border, uint format, uint type, IntPtr data);

	[DllImport("opengl32")]
	private static extern void glPixelStorei(uint name, int value);

	[DllImport("opengl32")]
	private static extern void glReadBuffer(uint mode);

	[DllImport("opengl32")]
	private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, IntPtr data);

	[DllImport("opengl32")]
	private static extern void glClearColor(float r, float g, float b, float a);

	[DllImport("opengl32")]
	private static extern void glClear(uint mask);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void GenFn(int count, out uint id);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void DeleteFn(int count, ref uint id);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void BindFn(uint target, uint id);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void FramebufferTextureFn(uint target, uint attachment, uint textureTarget, uint texture, int level);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void RenderbufferStorageFn(uint target, uint format, int width, int height);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void FramebufferRenderbufferFn(uint target, uint attachment, uint renderTarget, uint renderbuffer);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate uint CheckFramebufferFn(uint target);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate void BufferDataFn(uint target, IntPtr size, IntPtr data, uint usage);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate IntPtr MapBufferFn(uint target, uint access);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	[return: MarshalAs(UnmanagedType.U1)]
	private delegate bool UnmapBufferFn(uint target);

	/// <summary>The core's get_current_framebuffer.</summary>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate UIntPtr CurrentFramebufferFn();

	/// <summary>The core's get_proc_address.</summary>
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate IntPtr ProcAddressFn(IntPtr name);

	private const uint GlVersion = 0x1F02, GlRenderer = 0x1F01, GlVendor = 0x1F00;
	private const uint Texture2D = 0x0DE1, Rgba = 0x1908, UnsignedByte = 0x1401;
	private const uint TextureMinFilter = 0x2801, TextureMagFilter = 0x2800, Nearest = 0x2600;
	private const uint FramebufferTarget = 0x8D40, ReadFramebuffer = 0x8CA8, DrawFramebuffer = 0x8CA9;
	private const uint ColorAttachment0 = 0x8CE0, DepthStencilAttachment = 0x821A, RenderbufferTarget = 0x8D41, Depth24Stencil8 = 0x88F0;
	private const uint FramebufferComplete = 0x8CD5;
	private const uint DrawFramebufferBinding = 0x8CA6, ReadFramebufferBinding = 0x8CAA, PixelPackBufferBinding = 0x88ED, PackAlignment = 0x0D05, PackRowLength = 0x0D02;
	private const uint PixelPackBuffer = 0x88EB, StreamRead = 0x88E1, ReadOnly = 0x88B8;
	private const uint ColorBufferBit = 0x4000, DepthBufferBit = 0x100, StencilBufferBit = 0x400;

	#endregion

	private static readonly object Lock = new object();

	private static bool _tried;

	private static IntPtr _window;

	private static IntPtr _dc;

	private static IntPtr _context;

	private static IntPtr _opengl;

	private static GenFn _genFramebuffers;
	private static GenFn _genRenderbuffers;
	private static GenFn _genBuffers;
	private static DeleteFn _deleteFramebuffers;
	private static DeleteFn _deleteRenderbuffers;
	private static DeleteFn _deleteBuffers;
	private static BindFn _bindFramebuffer;
	private static BindFn _bindRenderbuffer;
	private static BindFn _bindBuffer;
	private static FramebufferTextureFn _framebufferTexture;
	private static RenderbufferStorageFn _renderbufferStorage;
	private static FramebufferRenderbufferFn _framebufferRenderbuffer;
	private static CheckFramebufferFn _checkFramebuffer;
	private static BufferDataFn _bufferData;
	private static MapBufferFn _mapBuffer;
	private static UnmapBufferFn _unmapBuffer;

	private static uint _framebuffer;

	private static uint _texture;

	private static uint _depth;

	private static readonly uint[] _packs = new uint[2];

	private static int _packBytes;

	private static int _width;

	private static int _height;

	/// <summary>Which pack buffer gets the next picture, and what's waiting in the other one.</summary>
	private static int _pack;

	private static int _waitingW;

	private static int _waitingH;

	// kept alive: the core holds raw pointers to them
	internal static readonly CurrentFramebufferFn CurrentFramebuffer = () => (UIntPtr)_framebuffer;

	internal static readonly ProcAddressFn ProcAddress = FindProc;

	internal static readonly IntPtr CurrentFramebufferPointer = Marshal.GetFunctionPointerForDelegate(CurrentFramebuffer);

	internal static readonly IntPtr ProcAddressPointer = Marshal.GetFunctionPointerForDelegate(ProcAddress);

	/// <summary>OpenGL 3 or better is here (else the core draws on the processor: slower, but it works).</summary>
	public static bool Available => _context != IntPtr.Zero;

	/// <summary>The graphics card, for the logs and the window ("" when there's none).</summary>
	public static string Renderer { get; private set; } = "";

	/// <summary>Why there's no OpenGL (null when there is).</summary>
	public static string Problem { get; private set; }

	/// <summary>Makes the context once. On the main thread: that thread keeps the hidden window alive.</summary>
	public static bool Prepare()
	{
		lock (Lock)
		{
			if (_tried)
			{
				return Available;
			}
			_tried = true;
			try
			{
				Create();
			}
			catch (Exception ex)
			{
				Problem = ex.Message;
				_context = IntPtr.Zero;
			}
			return Available;
		}
	}

	private static void Create()
	{
		// WS_POPUP, never shown; the system's STATIC class needs no window procedure of ours
		_window = CreateWindowExW(0, "STATIC", "IsaClient console", 0x80000000u, 0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		if (_window == IntPtr.Zero)
		{
			throw new InvalidOperationException("No hidden window (error " + Marshal.GetLastWin32Error() + ")");
		}
		_dc = GetDC(_window);
		PixelFormatDescriptor pfd = new PixelFormatDescriptor
		{
			Size = (ushort)Marshal.SizeOf(typeof(PixelFormatDescriptor)),
			Version = 1,
			Flags = 0x4u | 0x20u | 0x1u, // draw to window, OpenGL, double buffer
			PixelType = 0,
			ColorBits = 32,
			AlphaBits = 8,
			DepthBits = 24,
			StencilBits = 8
		};
		int format = ChoosePixelFormat(_dc, ref pfd);
		if (format == 0 || !SetPixelFormat(_dc, format, ref pfd))
		{
			throw new InvalidOperationException("No OpenGL pixel format (error " + Marshal.GetLastWin32Error() + ")");
		}
		IntPtr context = wglCreateContext(_dc);
		if (context == IntPtr.Zero)
		{
			throw new InvalidOperationException("No OpenGL context (error " + Marshal.GetLastWin32Error() + ")");
		}
		if (!wglMakeCurrent(_dc, context))
		{
			wglDeleteContext(context);
			throw new InvalidOperationException("Couldn't use the OpenGL context (error " + Marshal.GetLastWin32Error() + ")");
		}
		try
		{
			string version = Text(GlVersion);
			Renderer = (Text(GlVendor) + " " + Text(GlRenderer) + " (OpenGL " + version + ")").Trim();
			int major = 0;
			foreach (char c in version)
			{
				if (!char.IsDigit(c))
				{
					break;
				}
				major = major * 10 + (c - '0');
			}
			if (major < 3)
			{
				throw new InvalidOperationException("OpenGL 3 is needed, this PC has " + (version.Length > 0 ? version : "none"));
			}
			_opengl = GetModuleHandle("opengl32.dll");
			LoadFunctions();
		}
		catch
		{
			wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
			wglDeleteContext(context);
			throw;
		}
		wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
		_context = context;
	}

	private static string Text(uint name)
	{
		IntPtr p = glGetString(name);
		return (p == IntPtr.Zero) ? "" : (Marshal.PtrToStringAnsi(p) ?? "");
	}

	private static void LoadFunctions()
	{
		T Fn<T>(string name) where T : Delegate
		{
			IntPtr p = Find(name);
			if (p == IntPtr.Zero)
			{
				throw new InvalidOperationException("OpenGL has no " + name);
			}
			return (T)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
		}
		_genFramebuffers = Fn<GenFn>("glGenFramebuffers");
		_genRenderbuffers = Fn<GenFn>("glGenRenderbuffers");
		_genBuffers = Fn<GenFn>("glGenBuffers");
		_deleteFramebuffers = Fn<DeleteFn>("glDeleteFramebuffers");
		_deleteRenderbuffers = Fn<DeleteFn>("glDeleteRenderbuffers");
		_deleteBuffers = Fn<DeleteFn>("glDeleteBuffers");
		_bindFramebuffer = Fn<BindFn>("glBindFramebuffer");
		_bindRenderbuffer = Fn<BindFn>("glBindRenderbuffer");
		_bindBuffer = Fn<BindFn>("glBindBuffer");
		_framebufferTexture = Fn<FramebufferTextureFn>("glFramebufferTexture2D");
		_renderbufferStorage = Fn<RenderbufferStorageFn>("glRenderbufferStorage");
		_framebufferRenderbuffer = Fn<FramebufferRenderbufferFn>("glFramebufferRenderbuffer");
		_checkFramebuffer = Fn<CheckFramebufferFn>("glCheckFramebufferStatus");
		_bufferData = Fn<BufferDataFn>("glBufferData");
		_mapBuffer = Fn<MapBufferFn>("glMapBuffer");
		_unmapBuffer = Fn<UnmapBufferFn>("glUnmapBuffer");
	}

	private static IntPtr Find(string name)
	{
		IntPtr p = wglGetProcAddress(name);
		long v = p.ToInt64();
		if (v >= -1 && v <= 3)
		{
			// the base functions aren't given by wglGetProcAddress: they're opengl32's own exports
			p = (_opengl != IntPtr.Zero) ? GetProcAddress(_opengl, name) : IntPtr.Zero;
		}
		return p;
	}

	private static IntPtr FindProc(IntPtr name)
	{
		try
		{
			return Find(Marshal.PtrToStringAnsi(name));
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>The context is this thread's from now on (the machine's, for a game).</summary>
	public static bool Bind() => Available && wglMakeCurrent(_dc, _context);

	/// <summary>Lets go of the context (the next game runs on another thread).</summary>
	public static void Release()
	{
		if (Available)
		{
			wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
		}
	}

	/// <summary>Makes the core's framebuffer, at least <paramref name="width"/> x <paramref name="height"/>. Keeps its id when it grows.</summary>
	public static void Size(int width, int height)
	{
		if (_framebuffer != 0 && width <= _width && height <= _height)
		{
			return;
		}
		width = Math.Max(width, _width);
		height = Math.Max(height, _height);
		if (_framebuffer == 0)
		{
			_genFramebuffers(1, out _framebuffer);
			glGenTextures(1, out _texture);
			_genRenderbuffers(1, out _depth);
			_genBuffers(1, out _packs[0]);
			_genBuffers(1, out _packs[1]);
		}
		glGetIntegerv(DrawFramebufferBinding, out int draw);
		glGetIntegerv(ReadFramebufferBinding, out int read);
		glBindTexture(Texture2D, _texture);
		glTexParameteri(Texture2D, TextureMinFilter, (int)Nearest);
		glTexParameteri(Texture2D, TextureMagFilter, (int)Nearest);
		glTexImage2D(Texture2D, 0, (int)Rgba, width, height, 0, Rgba, UnsignedByte, IntPtr.Zero);
		glBindTexture(Texture2D, 0);
		_bindRenderbuffer(RenderbufferTarget, _depth);
		_renderbufferStorage(RenderbufferTarget, Depth24Stencil8, width, height);
		_bindRenderbuffer(RenderbufferTarget, 0);
		_bindFramebuffer(FramebufferTarget, _framebuffer);
		_framebufferTexture(FramebufferTarget, ColorAttachment0, Texture2D, _texture, 0);
		_framebufferRenderbuffer(FramebufferTarget, DepthStencilAttachment, RenderbufferTarget, _depth);
		uint status = _checkFramebuffer(FramebufferTarget);
		if (status != FramebufferComplete)
		{
			throw new InvalidOperationException("The framebuffer isn't usable (0x" + status.ToString("x") + ")");
		}
		glClearColor(0f, 0f, 0f, 1f);
		glClear(ColorBufferBit | DepthBufferBit | StencilBufferBit);
		_bindFramebuffer(DrawFramebuffer, (uint)draw);
		_bindFramebuffer(ReadFramebuffer, (uint)read);
		_width = width;
		_height = height;
		_waitingW = 0;
		_waitingH = 0;
	}

	/// <summary>
	/// Starts reading the picture the core just drew, and gives the one before it (RGBA as XNA's Color packs it, top row
	/// first). Leaves OpenGL as the core had it. False while there's no picture yet.
	/// </summary>
	public static unsafe bool Read(int width, int height, bool bottomLeft, ref uint[] into, out int gotWidth, out int gotHeight)
	{
		gotWidth = 0;
		gotHeight = 0;
		width = Math.Min(width, _width);
		height = Math.Min(height, _height);
		if (width <= 0 || height <= 0)
		{
			return false;
		}
		glGetIntegerv(ReadFramebufferBinding, out int read);
		glGetIntegerv(PixelPackBufferBinding, out int pack);
		glGetIntegerv(PackAlignment, out int alignment);
		glGetIntegerv(PackRowLength, out int rowLength);
		int bytes = width * height * 4;
		if (bytes > _packBytes)
		{
			// a bigger picture than any so far: room in both, and the one waiting is gone
			foreach (uint b in _packs)
			{
				_bindBuffer(PixelPackBuffer, b);
				_bufferData(PixelPackBuffer, (IntPtr)bytes, IntPtr.Zero, StreamRead);
			}
			_packBytes = bytes;
			_waitingW = 0;
			_waitingH = 0;
		}
		_bindFramebuffer(ReadFramebuffer, _framebuffer);
		glReadBuffer(ColorAttachment0);
		glPixelStorei(PackAlignment, 4);
		glPixelStorei(PackRowLength, 0);
		_bindBuffer(PixelPackBuffer, _packs[_pack]);
		glReadPixels(0, 0, width, height, Rgba, UnsignedByte, IntPtr.Zero);
		int previous = _pack ^ 1;
		bool got = false;
		if (_waitingW > 0)
		{
			_bindBuffer(PixelPackBuffer, _packs[previous]);
			IntPtr mapped = _mapBuffer(PixelPackBuffer, ReadOnly);
			if (mapped != IntPtr.Zero)
			{
				int w = _waitingW;
				int h = _waitingH;
				if (into == null || into.Length < w * h)
				{
					into = new uint[w * h];
				}
				uint* source = (uint*)mapped;
				fixed (uint* target = into)
				{
					for (int y = 0; y < h; y++)
					{
						uint* row = source + (bottomLeft ? (h - 1 - y) : y) * w;
						uint* to = target + y * w;
						for (int x = 0; x < w; x++)
						{
							to[x] = row[x] | 0xFF000000u;
						}
					}
				}
				_unmapBuffer(PixelPackBuffer);
				gotWidth = w;
				gotHeight = h;
				got = true;
			}
		}
		_waitingW = width;
		_waitingH = height;
		_pack = previous;
		_bindBuffer(PixelPackBuffer, (uint)pack);
		_bindFramebuffer(ReadFramebuffer, (uint)read);
		glPixelStorei(PackAlignment, alignment);
		glPixelStorei(PackRowLength, rowLength);
		return got;
	}

	/// <summary>Frees what the core drew on (between games the context itself stays).</summary>
	public static void FreeTargets()
	{
		if (_framebuffer == 0)
		{
			return;
		}
		_deleteFramebuffers(1, ref _framebuffer);
		glDeleteTextures(1, ref _texture);
		_deleteRenderbuffers(1, ref _depth);
		_deleteBuffers(1, ref _packs[0]);
		_deleteBuffers(1, ref _packs[1]);
		_framebuffer = 0;
		_texture = 0;
		_depth = 0;
		_packs[0] = 0;
		_packs[1] = 0;
		_packBytes = 0;
		_width = 0;
		_height = 0;
		_waitingW = 0;
		_waitingH = 0;
	}
}
