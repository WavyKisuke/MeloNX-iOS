# Touch buffer regression checks

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project tests/MeloNX.Touch.Tests -c Release
```

This dependency-free executable compiles the production touch buffer, interfaces, coordinate conversion, and HLE touch manager directly; the emulator device is a recording test double. It checks stable contact IDs, snapshot immutability and reuse, ignored late moves, invalid input, reset, allocation-free unchanged polling, concurrent UI/input-thread access, guest contact lifecycle, and focus loss/recovery. It does not replace an iOS device test.
