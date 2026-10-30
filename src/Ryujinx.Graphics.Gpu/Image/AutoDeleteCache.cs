using Ryujinx.Common.Logging;
using Ryujinx.Graphics.GAL;
using Ryujinx.Memory;
using System;
using System.Collections;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Gpu.Image
{
    /// <summary>
    /// An entry on the short duration texture cache.
    /// </summary>
    class ShortTextureCacheEntry
    {
        public bool IsAutoDelete;
        public readonly TextureDescriptor Descriptor;
        public readonly int InvalidatedSequence;
        public readonly Texture Texture;

        public ShortTextureCacheEntry(TextureDescriptor descriptor, Texture texture)
        {
            Descriptor = descriptor;
            InvalidatedSequence = texture.InvalidatedSequence;
            Texture = texture;
        }

        public ShortTextureCacheEntry(Texture texture)
        {
            IsAutoDelete = true;
            InvalidatedSequence = texture.InvalidatedSequence;
            Texture = texture;
        }
    }

    class AutoDeleteCache : IEnumerable<Texture>
    {
        private const int MaxCapacity = 2048;
        private const ulong MiB = 1024 * 1024;
        private const ulong GiB = 1024 * 1024 * 1024;
        private const ulong ProcessConstrainedMemoryThreshold = 8UL * GiB;
        private const ulong MaxProcessTextureSizeCapacity = 2UL * GiB;
        private const double ProcessMemoryScaleFactor = 0.30d;
        private ulong MaxTextureSizeCapacity = 4UL * GiB;
        private const ulong MinTextureSizeCapacity = 512 * 1024 * 1024;
        private const ulong DefaultTextureSizeCapacity = 1 * GiB;
        private const ulong MinUnifiedTextureSizeCapacity = 256 * MiB;
        private const ulong MaxUnifiedTextureSizeCapacity = 768 * MiB;
        private const ulong TextureSizeCapacity6GiB = 4 * GiB;
        private const ulong TextureSizeCapacity8GiB = 6 * GiB;
        private const ulong TextureSizeCapacity12GiB = 12 * GiB;

        private const float MemoryScaleFactor = 0.50f;
        private const int UnifiedMemoryDivisor = 16;
        private const int ConstrainedMemoryDivisor = 8;
        private ulong _maxCacheMemoryUsage = DefaultTextureSizeCapacity;

        private readonly LinkedList<Texture> _textures;
        private ulong _totalSize;

        private HashSet<ShortTextureCacheEntry> _shortCacheBuilder;
        private HashSet<ShortTextureCacheEntry> _shortCache;
        private readonly Dictionary<TextureDescriptor, ShortTextureCacheEntry> _shortCacheLookup;

        public void Initialize(GpuContext context, ulong cpuMemorySize)
        {
            bool isAppleUnifiedMemory = (OperatingSystem.IsIOS() || OperatingSystem.IsMacOS()) &&
                                        context.Capabilities.MemoryType == SystemMemoryType.UnifiedMemory;

            if (isAppleUnifiedMemory)
            {
                ulong? hostShare = GraphicsConfig.EnableHostMemoryBudgetTracking
                    ? HostMemoryBudget.GetShare(
                        ConstrainedMemoryDivisor,
                        MinUnifiedTextureSizeCapacity,
                        MaxUnifiedTextureSizeCapacity)
                    : null;

                _maxCacheMemoryUsage = hostShare ?? Math.Clamp(
                    cpuMemorySize / UnifiedMemoryDivisor,
                    MinUnifiedTextureSizeCapacity,
                    MaxUnifiedTextureSizeCapacity);

                string basis = hostShare.HasValue
                    ? $"{HostMemoryBudget.TotalBytes / MiB} MiB process budget"
                    : "guest memory size";

                Logger.Info?.Print(LogClass.Gpu, $"AutoDelete cache memory limit: {_maxCacheMemoryUsage / MiB} MiB (Apple unified memory, from {basis})");
                return;
            }

            ulong cpuMemorySizeGiB = cpuMemorySize / GiB;

            if (cpuMemorySizeGiB < 6 || context.Capabilities.MaximumGpuMemory == 0)
            {
                _maxCacheMemoryUsage = DefaultTextureSizeCapacity;
                return;
            }
            else if (cpuMemorySizeGiB == 6)
            {
                MaxTextureSizeCapacity = TextureSizeCapacity6GiB;
            }
            else if (cpuMemorySizeGiB == 8)
            {
                MaxTextureSizeCapacity = TextureSizeCapacity8GiB;
            }
            else
            {
                MaxTextureSizeCapacity = TextureSizeCapacity12GiB;
            }

            ulong cacheMemory = (ulong)(context.Capabilities.MaximumGpuMemory * MemoryScaleFactor);
            _maxCacheMemoryUsage = Math.Clamp(cacheMemory, MinTextureSizeCapacity, MaxTextureSizeCapacity);

            long totalAvailableMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

            if (totalAvailableMemory > 0 && (ulong)totalAvailableMemory <= ProcessConstrainedMemoryThreshold)
            {
                ulong processBudget = (ulong)(totalAvailableMemory * ProcessMemoryScaleFactor);
                processBudget = Math.Clamp(processBudget, MinTextureSizeCapacity, MaxProcessTextureSizeCapacity);
                _maxCacheMemoryUsage = Math.Min(_maxCacheMemoryUsage, processBudget);
            }

            Logger.Info?.Print(LogClass.Gpu, $"AutoDelete cache memory limit: {_maxCacheMemoryUsage / MiB} MiB");
        }

        public AutoDeleteCache()
        {
            _textures = [];
            _shortCacheBuilder = [];
            _shortCache = [];
            _shortCacheLookup = new Dictionary<TextureDescriptor, ShortTextureCacheEntry>();
        }

        public void Add(Texture texture)
        {
            texture.CacheSize = texture.GetEstimatedHostSize();
            _totalSize += texture.CacheSize;
            texture.IncrementReferenceCount();
            texture.CacheNode = _textures.AddLast(texture);
            EnforceCapacity();
        }

        public void Lift(Texture texture)
        {
            if (texture.CacheNode != null)
            {
                ulong oldSize = texture.CacheSize;
                texture.CacheSize = texture.GetEstimatedHostSize();
                _totalSize = _totalSize - oldSize + texture.CacheSize;

                if (texture.CacheNode != _textures.Last)
                {
                    _textures.Remove(texture.CacheNode);
                    _textures.AddLast(texture.CacheNode);
                }

                EnforceCapacity();
            }
            else
            {
                Add(texture);
            }
        }

        private void RemoveLeastUsedTexture()
        {
            Texture oldestTexture = _textures.First.Value;
            _totalSize -= oldestTexture.CacheSize;
            oldestTexture.CacheSize = 0;

            if (!oldestTexture.CheckModified(false))
            {
                oldestTexture.Group.SynchronizeDependents(oldestTexture);
                oldestTexture.FlushModified(false);
            }

            _textures.RemoveFirst();
            oldestTexture.DecrementReferenceCount();
            oldestTexture.CacheNode = null;
        }

        private void EnforceCapacity()
        {
            while (_textures.Count > MaxCapacity ||
                   (_totalSize > _maxCacheMemoryUsage && _textures.Count > 0))
            {
                RemoveLeastUsedTexture();
            }
        }

        private const ulong TrimFloorDivisor = 8;

        public ulong Trim(MemoryPressureLevel level)
        {
            if (!GraphicsConfig.EnableHostMemoryBudgetTracking)
            {
                return 0;
            }

            ulong target = level switch
            {
                MemoryPressureLevel.Critical => _totalSize / 2,
                MemoryPressureLevel.Warning => _totalSize - (_totalSize / 4),
                _ => _maxCacheMemoryUsage,
            };

            target = Math.Max(target, _maxCacheMemoryUsage / TrimFloorDivisor);

            if (target >= _totalSize)
            {
                return 0;
            }

            ulong before = _totalSize;

            while (_totalSize > target && _textures.Count > 0)
            {
                RemoveLeastUsedTexture();
            }

            return before - _totalSize;
        }

        public bool Remove(Texture texture, bool flush)
        {
            if (texture.CacheNode == null)
            {
                return false;
            }

            if (flush)
            {
                texture.FlushModified(false);
            }

            _textures.Remove(texture.CacheNode);
            _totalSize -= texture.CacheSize;
            texture.CacheSize = 0;
            texture.CacheNode = null;

            return texture.DecrementReferenceCount();
        }

        public Texture FindShortCache(in TextureDescriptor descriptor)
        {
            if (_shortCacheLookup.Count > 0 && _shortCacheLookup.TryGetValue(descriptor, out ShortTextureCacheEntry entry))
            {
                if (entry.InvalidatedSequence == entry.Texture.InvalidatedSequence)
                {
                    return entry.Texture;
                }
                else
                {
                    _shortCacheLookup.Remove(descriptor);
                }
            }

            return null;
        }

        public void RemoveShortCache(Texture texture)
        {
            bool removed = _shortCache.Remove(texture.ShortCacheEntry);
            removed |= _shortCacheBuilder.Remove(texture.ShortCacheEntry);

            if (removed)
            {
                texture.DecrementReferenceCount();

                if (!texture.ShortCacheEntry.IsAutoDelete)
                {
                    _shortCacheLookup.Remove(texture.ShortCacheEntry.Descriptor);
                }

                texture.ShortCacheEntry = null;
            }
        }

        public void AddShortCache(Texture texture, ref TextureDescriptor descriptor)
        {
            ShortTextureCacheEntry entry = new(descriptor, texture);
            _shortCacheBuilder.Add(entry);
            _shortCacheLookup.Add(entry.Descriptor, entry);
            texture.ShortCacheEntry = entry;
            texture.IncrementReferenceCount();
        }

        public void AddShortCache(Texture texture)
        {
            // A texture should only get a new short-cache entry when it does not
            // already have one. This prevents duplicate reference-count ownership.
            if (texture.ShortCacheEntry == null)
            {
                ShortTextureCacheEntry entry = new(texture);
                _shortCacheBuilder.Add(entry);
                texture.ShortCacheEntry = entry;
                texture.IncrementReferenceCount();
            }
        }

        public void ProcessShortCache()
        {
            HashSet<ShortTextureCacheEntry> toRemove = _shortCache;

            foreach (ShortTextureCacheEntry entry in toRemove)
            {
                entry.Texture.DecrementReferenceCount();

                if (entry.IsAutoDelete)
                {
                    Remove(entry.Texture, false);
                }
                else
                {
                    _shortCacheLookup.Remove(entry.Descriptor);
                }

                entry.Texture.ShortCacheEntry = null;
            }

            toRemove.Clear();
            _shortCache = _shortCacheBuilder;
            _shortCacheBuilder = toRemove;
        }

        public IEnumerator<Texture> GetEnumerator()
        {
            return _textures.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _textures.GetEnumerator();
        }
    }
}
