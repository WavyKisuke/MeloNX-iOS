# LemonX Performance Work Plan

This branch is for independently implementing performance and stability improvements for MeloNX based on observable behavior and open-source iOS emulator work. It does not copy proprietary code or copyrighted game content from any IPA.

## Priorities

1. iOS memory stability and reduced allocation pressure.
2. Shader/pipeline compilation and cache behavior.
3. GPU frame pacing and avoiding unnecessary synchronization.
4. Sustained performance under thermal pressure.
5. Repeatable benchmarking before enabling aggressive optimizations by default.

## Reference observations

The LemonX IPA supplied for comparison is treated as a black-box performance reference. The useful targets are lower stutter, fewer memory-related failures, better sustained frame pacing, and faster shader warm-up.

## Guardrails

- Keep the existing emulator architecture intact.
- Do not distribute firmware, keys, games, DLC, or shader caches.
- Prefer upstream/open-source implementations that can be reviewed and maintained.
- Avoid experimental memory changes until they have a fallback path.

## Benchmark protocol

For each candidate change, compare a cold launch and a warmed launch, record FPS/frame-time stability, memory pressure/crashes, and shader-compilation stutter. Test at the same resolution and game settings so the comparison is meaningful.
