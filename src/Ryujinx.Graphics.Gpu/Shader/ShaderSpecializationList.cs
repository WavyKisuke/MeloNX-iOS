using System.Collections;
using System.Collections.Generic;

namespace Ryujinx.Graphics.Gpu.Shader
{
    /// <summary>
    /// List of cached shader programs that differs only by specialization state.
    /// </summary>
    class ShaderSpecializationList : IEnumerable<CachedShaderProgram>
    {
        private readonly List<CachedShaderProgram> _entries = [];
        private CachedShaderProgram _lastGraphicsMatch;
        private CachedShaderProgram _lastComputeMatch;

        /// <summary>
        /// Adds a program to the list.
        /// </summary>
        /// <param name="program">Program to be added</param>
        public void Add(CachedShaderProgram program)
        {
            _entries.Add(program);
        }

        /// <summary>
        /// Tries to find an existing 3D program on the cache.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="poolState">Texture pool state</param>
        /// <param name="graphicsState">Graphics state</param>
        /// <param name="program">Cached program, if found</param>
        /// <returns>True if a compatible program is found, false otherwise</returns>
        public bool TryFindForGraphics(
            GpuChannel channel,
            ref GpuChannelPoolState poolState,
            ref GpuChannelGraphicsState graphicsState,
            out CachedShaderProgram program)
        {
            CachedShaderProgram lastMatch = _lastGraphicsMatch;

            if (lastMatch != null && MatchesGraphics(lastMatch, channel, ref poolState, ref graphicsState))
            {
                program = lastMatch;
                return true;
            }

            foreach (CachedShaderProgram entry in _entries)
            {
                if (entry == lastMatch)
                {
                    continue;
                }

                if (MatchesGraphics(entry, channel, ref poolState, ref graphicsState))
                {
                    _lastGraphicsMatch = entry;
                    program = entry;
                    return true;
                }
            }

            program = default;
            return false;
        }

        /// <summary>
        /// Tries to find an existing compute program on the cache.
        /// </summary>
        /// <param name="channel">GPU channel</param>
        /// <param name="poolState">GPU channel state</param>
        /// <param name="computeState">Compute state</param>
        /// <param name="program">Cached program, if found</param>
        /// <returns>True if a compatible program is found, false otherwise</returns>
        public bool TryFindForCompute(
            GpuChannel channel,
            GpuChannelPoolState poolState,
            GpuChannelComputeState computeState,
            out CachedShaderProgram program)
        {
            CachedShaderProgram lastMatch = _lastComputeMatch;

            if (lastMatch != null && lastMatch.SpecializationState.MatchesCompute(channel, ref poolState, computeState, true))
            {
                program = lastMatch;
                return true;
            }

            foreach (CachedShaderProgram entry in _entries)
            {
                if (entry == lastMatch)
                {
                    continue;
                }

                if (entry.SpecializationState.MatchesCompute(channel, ref poolState, computeState, true))
                {
                    _lastComputeMatch = entry;
                    program = entry;
                    return true;
                }
            }

            program = default;
            return false;
        }

        private static bool MatchesGraphics(
            CachedShaderProgram entry,
            GpuChannel channel,
            ref GpuChannelPoolState poolState,
            ref GpuChannelGraphicsState graphicsState)
        {
            bool vertexAsCompute = entry.VertexAsCompute != null;
            bool usesDrawParameters = entry.Shaders[1]?.Info.UsesDrawParameters ?? false;

            return entry.SpecializationState.MatchesGraphics(
                channel,
                ref poolState,
                ref graphicsState,
                vertexAsCompute,
                usesDrawParameters,
                checkTextures: true);
        }

        public IEnumerator<CachedShaderProgram> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
