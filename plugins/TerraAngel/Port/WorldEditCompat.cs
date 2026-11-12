// What TerraAngel's world edits (Source/WorldEdits) use that IsaClient's game and .NET Framework 4.8 don't have, written
// again with the same names so the world edit files stay as TerraAngel wrote them (and a fork's copies compile too):
//  - TerraAngel's game keeps tiles in a native map of TileData structs; here tiles are Terraria's Tile objects.
//  - System.Threading.RateLimiting (token buckets), ValueTask and IAsyncDisposable (.NET Core).
//  - SixLabors.ImageSharp (loading, resizing and rotating pictures): GDI+ underneath.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace TerraAngel.WorldEdits
{
    /// <summary>TerraAngel's native tile map, as a grid of Terraria tiles (or a view of Main.tile).</summary>
    public sealed class NativeTileMap : IDisposable
    {
        private readonly Tile[,] _tiles;

        public int Width { get; }

        public int Height { get; }

        public NativeTileMap(int width, int height)
        {
            Width = width;
            Height = height;
            _tiles = new Tile[width, height];
        }

        private NativeTileMap(Tile[,] tiles)
        {
            _tiles = tiles;
            Width = tiles.GetLength(0);
            Height = tiles.GetLength(1);
        }

        /// <summary>The tile at (x, y), made when it was never set.</summary>
        public Tile this[int x, int y] => GetTileRef(x, y);

        public ref Tile GetTileRef(int x, int y)
        {
            ref Tile tile = ref _tiles[x, y];
            if (tile == null)
            {
                tile = new Tile();
            }
            return ref tile;
        }

        public static implicit operator NativeTileMap(Tile[,] tiles) => new NativeTileMap(tiles);

        public void Dispose()
        {
        }
    }

    public static class NativeTileMapExtensions
    {
        extension(Tile tile)
        {
            /// <summary>TileData's storage: never empty here (every cell of a section is a Tile).</summary>
            public object Data => tile;
        }

        /// <summary>Main.tile.GetTileRef(x, y) of TerraAngel's game.</summary>
        public static ref Tile GetTileRef(this Tile[,] tiles, int x, int y)
        {
            ref Tile tile = ref tiles[x, y];
            if (tile == null)
            {
                tile = new Tile();
            }
            return ref tile;
        }
    }
}

namespace System
{
    public interface IAsyncDisposable
    {
        System.Threading.Tasks.ValueTask DisposeAsync();
    }
}

namespace System.Threading.Tasks
{
    /// <summary>ValueTask for .NET Framework: a Task underneath.</summary>
    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder))]
    public readonly struct ValueTask
    {
        private readonly Task _task;

        public ValueTask(Task task)
        {
            _task = task;
        }

        public static ValueTask CompletedTask => new ValueTask(Task.FromResult(0));

        public Task AsTask() => _task ?? Task.FromResult(0);

        public bool IsCompleted => AsTask().IsCompleted;

        public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) => AsTask().ConfigureAwait(continueOnCapturedContext);
    }

    [AsyncMethodBuilder(typeof(AsyncValueTaskMethodBuilder<>))]
    public readonly struct ValueTask<TResult>
    {
        private readonly Task<TResult> _task;

        public ValueTask(Task<TResult> task)
        {
            _task = task;
        }

        public ValueTask(TResult result)
        {
            _task = Task.FromResult(result);
        }

        public Task<TResult> AsTask() => _task;

        public bool IsCompleted => _task.IsCompleted;

        public TResult Result => _task.Result;

        public TaskAwaiter<TResult> GetAwaiter() => _task.GetAwaiter();

        public ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) => _task.ConfigureAwait(continueOnCapturedContext);
    }
}

namespace System.Runtime.CompilerServices
{
    public struct AsyncValueTaskMethodBuilder
    {
        private AsyncTaskMethodBuilder _builder;

        public static AsyncValueTaskMethodBuilder Create() => new AsyncValueTaskMethodBuilder { _builder = AsyncTaskMethodBuilder.Create() };

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);

        public void SetResult() => _builder.SetResult();

        public void SetException(Exception exception) => _builder.SetException(exception);

        public System.Threading.Tasks.ValueTask Task => new System.Threading.Tasks.ValueTask(_builder.Task);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }

    public struct AsyncValueTaskMethodBuilder<TResult>
    {
        private AsyncTaskMethodBuilder<TResult> _builder;

        public static AsyncValueTaskMethodBuilder<TResult> Create() => new AsyncValueTaskMethodBuilder<TResult> { _builder = AsyncTaskMethodBuilder<TResult>.Create() };

        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);

        public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);

        public void SetResult(TResult result) => _builder.SetResult(result);

        public void SetException(Exception exception) => _builder.SetException(exception);

        public System.Threading.Tasks.ValueTask<TResult> Task => new System.Threading.Tasks.ValueTask<TResult>(_builder.Task);

        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);

        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
    }
}

namespace System.Threading.RateLimiting
{
    internal enum QueueProcessingOrder
    {
        OldestFirst,
        NewestFirst
    }

    internal sealed class TokenBucketRateLimiterOptions
    {
        public int TokenLimit { get; set; }

        public int QueueLimit { get; set; }

        public QueueProcessingOrder QueueProcessingOrder { get; set; }

        public bool AutoReplenishment { get; set; }

        public TimeSpan ReplenishmentPeriod { get; set; }

        public int TokensPerPeriod { get; set; }
    }

    internal sealed class RateLimitLease : IDisposable
    {
        public RateLimitLease(bool acquired)
        {
            IsAcquired = acquired;
        }

        public bool IsAcquired { get; }

        public void Dispose()
        {
        }
    }

    internal abstract class RateLimiter : IDisposable, IAsyncDisposable
    {
        public abstract RateLimitLease AttemptAcquire(int permitCount = 1);

        public abstract System.Threading.Tasks.ValueTask<RateLimitLease> AcquireAsync(int permitCount = 1, CancellationToken cancellationToken = default);

        public virtual void Dispose()
        {
        }

        public System.Threading.Tasks.ValueTask DisposeAsync()
        {
            Dispose();
            return System.Threading.Tasks.ValueTask.CompletedTask;
        }
    }

    /// <summary>Tokens come back <see cref="TokenBucketRateLimiterOptions.TokensPerPeriod"/> at a time every period, up to the limit.</summary>
    internal sealed class TokenBucketRateLimiter : RateLimiter
    {
        private readonly TokenBucketRateLimiterOptions _options;

        private readonly object _lock = new object();

        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

        private double _tokens;

        private TimeSpan _last;

        public TokenBucketRateLimiter(TokenBucketRateLimiterOptions options)
        {
            _options = options;
            _tokens = options.TokenLimit;
        }

        private void Refill()
        {
            TimeSpan now = _clock.Elapsed;
            double periods = (now - _last).TotalMilliseconds / Math.Max(0.001, _options.ReplenishmentPeriod.TotalMilliseconds);
            if (periods >= 1.0)
            {
                _tokens = Math.Min(_options.TokenLimit, _tokens + Math.Floor(periods) * _options.TokensPerPeriod);
                _last += TimeSpan.FromMilliseconds(Math.Floor(periods) * _options.ReplenishmentPeriod.TotalMilliseconds);
            }
        }

        public override RateLimitLease AttemptAcquire(int permitCount = 1)
        {
            lock (_lock)
            {
                Refill();
                if (permitCount == 0)
                {
                    return new RateLimitLease(_tokens >= 1.0);
                }
                if (_tokens >= permitCount)
                {
                    _tokens -= permitCount;
                    return new RateLimitLease(true);
                }
                return new RateLimitLease(false);
            }
        }

        public override async System.Threading.Tasks.ValueTask<RateLimitLease> AcquireAsync(int permitCount = 1, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                RateLimitLease lease = AttemptAcquire(Math.Max(1, permitCount));
                if (lease.IsAcquired)
                {
                    return lease;
                }
                await Task.Delay(Math.Max(1, (int)Math.Min(50.0, _options.ReplenishmentPeriod.TotalMilliseconds)), cancellationToken);
            }
        }
    }
}

namespace SixLabors.ImageSharp.PixelFormats
{
    /// <summary>A pixel: R, G, B, A bytes (the same packing as XNA's Color).</summary>
    public struct Rgba32
    {
        public uint PackedValue;

        public Rgba32(byte r, byte g, byte b, byte a)
        {
            PackedValue = (uint)(r | (g << 8) | (b << 16) | (a << 24));
        }

        public byte R => (byte)PackedValue;

        public byte G => (byte)(PackedValue >> 8);

        public byte B => (byte)(PackedValue >> 16);

        public byte A => (byte)(PackedValue >> 24);
    }
}

namespace SixLabors.ImageSharp
{
    using SixLabors.ImageSharp.PixelFormats;

    public static class Image
    {
        /// <summary>Any picture GDI+ reads (png, jpg, gif, bmp...).</summary>
        public static Image<Rgba32> Load<TPixel>(string path) where TPixel : struct
        {
            using Bitmap bitmap = new Bitmap(path);
            return Image<Rgba32>.FromBitmap(bitmap);
        }
    }

    public sealed class Image<TPixel> : IDisposable where TPixel : struct
    {
        internal uint[] Pixels;

        public int Width { get; private set; }

        public int Height { get; private set; }

        public Image(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new uint[width * height];
        }

        public Rgba32 this[int x, int y]
        {
            get => new Rgba32 { PackedValue = Pixels[y * Width + x] };
            set => Pixels[y * Width + x] = value.PackedValue;
        }

        internal static Image<TPixel> FromBitmap(Bitmap source)
        {
            Image<TPixel> image = new Image<TPixel>(source.Width, source.Height);
            using Bitmap copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(copy))
            {
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            BitmapData data = copy.LockBits(new System.Drawing.Rectangle(0, 0, copy.Width, copy.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[copy.Width];
                for (int y = 0; y < copy.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, copy.Width);
                    for (int x = 0; x < copy.Width; x++)
                    {
                        // BGRA in memory -> RGBA.
                        uint p = (uint)row[x];
                        image.Pixels[y * copy.Width + x] = (p & 0xFF00FF00u) | ((p >> 16) & 0xFFu) | ((p & 0xFFu) << 16);
                    }
                }
            }
            finally
            {
                copy.UnlockBits(data);
            }
            return image;
        }

        internal Bitmap ToBitmap()
        {
            Bitmap bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[Width];
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        uint p = Pixels[y * Width + x];
                        row[x] = (int)((p & 0xFF00FF00u) | ((p >> 16) & 0xFFu) | ((p & 0xFFu) << 16));
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, Width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        internal void ReplaceWith(Bitmap bitmap)
        {
            Image<TPixel> other = FromBitmap(bitmap);
            Pixels = other.Pixels;
            Width = other.Width;
            Height = other.Height;
        }

        public Image<TPixel> Clone()
        {
            Image<TPixel> copy = new Image<TPixel>(Width, Height);
            Array.Copy(Pixels, copy.Pixels, Pixels.Length);
            return copy;
        }

        public void Mutate(Action<Processing.IImageProcessingContext> operation)
        {
            operation(new Processing.IImageProcessingContext(this));
        }

        public void Dispose()
        {
        }
    }
}

namespace SixLabors.ImageSharp.Processing
{
    using SixLabors.ImageSharp.PixelFormats;

    /// <summary>The operations TerraAngel's pixel art uses: resize and rotate.</summary>
    public sealed class IImageProcessingContext
    {
        private readonly Image<Rgba32> _image;

        internal IImageProcessingContext(object image)
        {
            _image = (Image<Rgba32>)image;
        }

        public IImageProcessingContext Resize(int width, int height)
        {
            using Bitmap source = _image.ToBitmap();
            using Bitmap target = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, 0, 0, target.Width, target.Height);
            }
            _image.ReplaceWith(target);
            return this;
        }

        /// <summary>Rotates by <paramref name="degrees"/> (clockwise); the picture grows to fit, the corners stay clear.</summary>
        public IImageProcessingContext Rotate(float degrees)
        {
            using Bitmap source = _image.ToBitmap();
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Abs(Math.Cos(radians));
            double sin = Math.Abs(Math.Sin(radians));
            int width = Math.Max(1, (int)Math.Ceiling(source.Width * cos + source.Height * sin));
            int height = Math.Max(1, (int)Math.Ceiling(source.Width * sin + source.Height * cos));
            using Bitmap target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(target))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TranslateTransform(width / 2f, height / 2f);
                g.RotateTransform(degrees);
                g.TranslateTransform(-source.Width / 2f, -source.Height / 2f);
                g.DrawImage(source, 0, 0, source.Width, source.Height);
            }
            _image.ReplaceWith(target);
            return this;
        }
    }
}
