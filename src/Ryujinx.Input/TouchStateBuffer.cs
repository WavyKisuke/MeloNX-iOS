using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ryujinx.Input
{
    /// <summary>
    /// Shares stable touch identities between UI callbacks and the input thread.
    /// Published snapshots are immutable and reused until the input changes.
    /// </summary>
    public sealed class TouchStateBuffer
    {
        public const int MaxTouches = 16;

        private readonly object _lock = new();
        private readonly Dictionary<int, Vector2> _touches = new();
        private KeyValuePair<int, Vector2>[] _snapshot = Array.Empty<KeyValuePair<int, Vector2>>();
        private bool _dirty;

        public void Begin(int id, Vector2 position)
        {
            if (id < 0 || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
            {
                return;
            }

            lock (_lock)
            {
                if (_touches.Count >= MaxTouches && !_touches.ContainsKey(id))
                {
                    return;
                }

                _touches[id] = position;
                _dirty = true;
            }
        }

        public void Move(int id, Vector2 position)
        {
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
            {
                return;
            }

            lock (_lock)
            {
                if (_touches.TryGetValue(id, out Vector2 previous) && previous != position)
                {
                    _touches[id] = position;
                    _dirty = true;
                }
            }
        }

        public void End(int id)
        {
            lock (_lock)
            {
                _dirty |= _touches.Remove(id);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _touches.Clear();
                _snapshot = Array.Empty<KeyValuePair<int, Vector2>>();
                _dirty = false;
            }
        }

        public ReadOnlyMemory<KeyValuePair<int, Vector2>> GetSnapshot()
        {
            lock (_lock)
            {
                if (_dirty)
                {
                    _snapshot = _touches.Count == 0
                        ? Array.Empty<KeyValuePair<int, Vector2>>()
                        : new KeyValuePair<int, Vector2>[_touches.Count];
                    ((ICollection<KeyValuePair<int, Vector2>>)_touches).CopyTo(_snapshot, 0);
                    _dirty = false;
                }

                return _snapshot;
            }
        }
    }
}
