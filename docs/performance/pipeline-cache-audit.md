# Pipeline cache performance audit

This document records the pipeline/shader-cache performance direction for iOS.

## Current strategy

- Reuse the existing shader specialization cache before doing a full specialization scan.
- Keep the existing persistent pipeline-cache path instead of introducing a second cache.
- Avoid aggressive cache-size reductions that would increase shader/pipeline recompilation.
- Measure pipeline creation and frame pacing before changing synchronization behavior.

## iOS considerations

MoltenVK can persist converted shader data through the Vulkan pipeline cache. Reusing that cache can avoid repeated SPIR-V-to-MSL conversion and reduce runtime shader-loading stalls.

The emulator should therefore prefer cache reuse and bounded memory behavior over repeatedly rebuilding pipelines.

## Next implementation targets

1. Verify pipeline cache reuse across app launches.
2. Identify duplicate pipeline creation on cache misses.
3. Measure CPU/GPU waits around first-use pipeline creation.
4. Only then adjust synchronization or warm-up behavior.
