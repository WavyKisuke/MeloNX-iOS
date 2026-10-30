using Ryujinx.Graphics.GAL;
using Ryujinx.Graphics.Gpu.Image;
using Ryujinx.Graphics.Gpu.Memory;
using Ryujinx.Graphics.Texture;
using Ryujinx.Memory.Range;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace Ryujinx.Graphics.Gpu
{
    /// <summary>
    /// GPU image presentation window.
    /// </summary>
    public class Window
    {
        private readonly GpuContext _context;
        private const int DefaultUnboundedPresentTargetFps = 60;

        /// <summary>
        /// Texture presented on the window.
        /// </summary>
        private readonly struct PresentationTexture
        {
            public TextureCache Cache { get; }
            public TextureInfo Info { get; }
            public MultiRange Range { get; }
            public ImageCrop Crop { get; }
            public Action<GpuContext, object> AcquireCallback { get; }
            public Action<object> ReleaseCallback { get; }
            public object UserObj { get; }

            public PresentationTexture(
                TextureCache cache,
                TextureInfo info,
                MultiRange range,
                ImageCrop crop,
                Action<GpuContext, object> acquireCallback,
                Action<object> releaseCallback,
                object userObj)
            {
                Cache = cache;
                Info = info;
                Range = range;
                Crop = crop;
                AcquireCallback = acquireCallback;
                ReleaseCallback = releaseCallback;
                UserObj = userObj;
            }
        }

        private readonly ConcurrentQueue<PresentationTexture> _frameQueue;
        private readonly Stopwatch _unboundedPresentTimer;
        private long _unboundedPresentTicksPerFrame;

        private int _framesAvailable;
        private long _lastUnboundedPresentTicks;
        private volatile bool _unboundedPresentMode;

        public bool IsFrameAvailable => _framesAvailable != 0;

        public Window(GpuContext context)
        {
            _context = context;
            _frameQueue = new ConcurrentQueue<PresentationTexture>();
            _unboundedPresentTimer = Stopwatch.StartNew();
            SetUnboundedPresentTargetFps(DefaultUnboundedPresentTargetFps);
        }

        public bool EnqueueFrameThreadSafe(
            ulong pid,
            ulong address,
            int width,
            int height,
            int stride,
            bool isLinear,
            int gobBlocksInY,
            Format format,
            byte bytesPerPixel,
            ImageCrop crop,
            Action<GpuContext, object> acquireCallback,
            Action<object> releaseCallback,
            object userObj)
        {
            if (!_context.PhysicalMemoryRegistry.TryGetValue(pid, out PhysicalMemory physicalMemory))
            {
                return false;
            }

            FormatInfo formatInfo = new(format, 1, 1, bytesPerPixel, 4);

            TextureInfo info = new(
                0UL,
                width,
                height,
                1,
                1,
                1,
                1,
                stride,
                isLinear,
                gobBlocksInY,
                1,
                1,
                Target.Texture2D,
                formatInfo);

            int size = SizeCalculator.GetBlockLinearTextureSize(
                width,
                height,
                1,
                1,
                1,
                1,
                1,
                bytesPerPixel,
                gobBlocksInY,
                1,
                1).TotalSize;

            MultiRange range = new(address, (ulong)size);

            _frameQueue.Enqueue(new PresentationTexture(
                physicalMemory.TextureCache,
                info,
                range,
                crop,
                acquireCallback,
                releaseCallback,
                userObj));

            return true;
        }

        public void Present(Action swapBuffersCallback)
        {
            _context.AdvanceSequence();

            if (_frameQueue.TryDequeue(out PresentationTexture pt))
            {
                pt.AcquireCallback(_context, pt.UserObj);
                Image.Texture texture = pt.Cache.FindOrCreateTexture(null, TextureSearchFlags.WithUpscale, pt.Info, 0, range: pt.Range);
                pt.Cache.Tick();

                ImageCrop crop = new(
                    (int)(pt.Crop.Left * texture.ScaleFactor),
                    (int)MathF.Ceiling(pt.Crop.Right * texture.ScaleFactor),
                    (int)(pt.Crop.Top * texture.ScaleFactor),
                    (int)MathF.Ceiling(pt.Crop.Bottom * texture.ScaleFactor),
                    pt.Crop.FlipX,
                    pt.Crop.FlipY,
                    pt.Crop.IsStretched,
                    pt.Crop.AspectRatioX,
                    pt.Crop.AspectRatioY);

                if (texture.Info.Width > pt.Info.Width || texture.Info.Height > pt.Info.Height)
                {
                    int top = crop.Top;
                    int bottom = crop.Bottom;
                    int left = crop.Left;
                    int right = crop.Right;

                    if (top == 0 && bottom == 0)
                    {
                        bottom = Math.Min(texture.Info.Height, pt.Info.Height);
                    }

                    if (left == 0 && right == 0)
                    {
                        right = Math.Min(texture.Info.Width, pt.Info.Width);
                    }

                    crop = new ImageCrop(left, right, top, bottom, crop.FlipX, crop.FlipY, crop.IsStretched, crop.AspectRatioX, crop.AspectRatioY);
                }

                _context.Renderer.Window.Present(texture.HostTexture, crop, swapBuffersCallback);
                pt.ReleaseCallback(pt.UserObj);
            }
        }

        public bool PresentLoop(Action swapBuffersCallback)
        {
            bool presented = false;

            if (!_unboundedPresentMode)
            {
                // Present at most one frame per loop iteration. If the producer has
                // already queued newer work, drop the surplus instead of performing
                // multiple host presents back-to-back.
                if (ConsumeFrameAvailable())
                {
                    Present(swapBuffersCallback);
                    presented = true;

                    while (ConsumeFrameAvailable())
                    {
                        DropFrame();
                    }
                }

                return presented;
            }

            long ticks = _unboundedPresentTimer.ElapsedTicks;
            long lastPresentTicks = Interlocked.Read(ref _lastUnboundedPresentTicks);
            bool shouldPresent = lastPresentTicks == 0 || ticks - lastPresentTicks >= Interlocked.Read(ref _unboundedPresentTicksPerFrame);

            while (ConsumeFrameAvailable())
            {
                if (shouldPresent)
                {
                    Present(swapBuffersCallback);
                    _lastUnboundedPresentTicks = ticks;
                    shouldPresent = false;
                    presented = true;
                }
                else
                {
                    DropFrame();
                }
            }

            return presented;
        }

        public void SetUnboundedPresentMode(bool enabled)
        {
            if (_unboundedPresentMode != enabled)
            {
                _unboundedPresentMode = enabled;
                Interlocked.Exchange(ref _lastUnboundedPresentTicks, 0);
            }
        }

        public void SetUnboundedPresentTargetFps(int targetFps)
        {
            targetFps = Math.Max(1, targetFps);
            Interlocked.Exchange(ref _unboundedPresentTicksPerFrame, Math.Max(1, Stopwatch.Frequency / targetFps));
            Interlocked.Exchange(ref _lastUnboundedPresentTicks, 0);
        }

        private void DropFrame()
        {
            if (_frameQueue.TryDequeue(out PresentationTexture pt))
            {
                pt.ReleaseCallback(pt.UserObj);
            }
        }

        public void SignalFrameReady()
        {
            Interlocked.Increment(ref _framesAvailable);
        }

        public bool ConsumeFrameAvailable()
        {
            while (true)
            {
                int available = Volatile.Read(ref _framesAvailable);

                if (available <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _framesAvailable, available - 1, available) == available)
                {
                    return true;
                }
            }
        }
    }
}