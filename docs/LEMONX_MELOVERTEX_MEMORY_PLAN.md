# iOS memory optimization plan

This branch tracks memory-focused changes for the MeloNX fork, using publicly documented MeloVertex/MeloNX work as implementation references.

## MeloVertex texture-cache optimization

MeloVertex 2.4.5 publicly describes an update to texture-cache memory management intended to reduce RAM usage in games. We should port the underlying open-source implementation/change where its source is available, rather than copying compiled/proprietary code from an IPA.

## MeloNX 2.7 memory architecture

The current upstream iOS direction also documents a RAM-limit workaround using NSExtensions and `xpc_shmem`, offloading guest RAM so shared memory does not contribute to the main app's physical footprint. This should be evaluated alongside the texture-cache work.

## Safety requirements

- Preserve correctness of texture lifetime and synchronization.
- Do not evict resources still referenced by in-flight GPU work.
- Prefer bounded caches and explicit reclaim paths under iOS memory pressure.
- Measure resident/physical footprint before and after each change.
- Keep changes independently implemented from public source; do not extract or copy proprietary code from LemonX binaries.

## Planned order

1. Locate and port the public MeloVertex texture-cache memory-management change.
2. Compare it with the current MeloNX texture cache and identify conflicts with existing GPU-buffer eviction.
3. Add memory-pressure-driven texture/cache reclamation with safe GPU synchronization.
4. Evaluate the NSExtension/xpc_shmem guest-RAM offload architecture.
5. Build and benchmark on iOS, then keep only changes that improve memory behavior without regressions.
