using System.Numerics;
using Ryujinx.Input;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var touches = new TouchStateBuffer();
Check(touches.GetSnapshot().IsEmpty, "New sessions must have no contacts.");
touches.Begin(10, new Vector2(10, 20));
touches.Begin(20, new Vector2(30, 40));
var original = touches.GetSnapshot();
Check(original.Equals(touches.GetSnapshot()), "Unchanged input should reuse its snapshot.");
touches.End(10);
touches.Move(20, new Vector2(50, 60));
var remaining = touches.GetSnapshot();
Check(remaining.Length == 1 && remaining.Span[0].Key == 20,
    "Releasing the first contact must not renumber another finger.");
Check(remaining.Span[0].Value == new Vector2(50, 60), "The surviving finger must keep receiving moves.");
Check(original.Length == 2 && original.Span[1].Value == new Vector2(30, 40),
    "A published snapshot must never change beneath its reader.");
touches.Move(10, Vector2.One);
Check(touches.GetSnapshot().Equals(remaining), "Late moves must not resurrect a released finger.");
touches.Move(20, new Vector2(50, 60));
Check(touches.GetSnapshot().Equals(remaining), "Identical moves should not invalidate the snapshot.");
touches.Begin(-1, Vector2.One);
touches.Begin(30, new Vector2(float.NaN, 0));
touches.Move(20, new Vector2(float.PositiveInfinity, 0));
Check(touches.GetSnapshot().Equals(remaining), "Invalid input must not enter the snapshot.");

long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 100_000; i++) _ = touches.GetSnapshot();
Check(GC.GetAllocatedBytesForCurrentThread() == before, "Unchanged input polling must allocate zero bytes.");
touches.Clear();
Check(touches.GetSnapshot().IsEmpty, "Reset must release every finger.");
Check(original.Length == 2, "Reset must leave previously published snapshots intact.");

await Task.WhenAll(
    Task.Run(() =>
    {
        for (int i = 0; i < 100_000; i++)
        {
            touches.Begin(i % 16, new Vector2(i, i));
            touches.Move(i % 16, new Vector2(i + 1, i + 1));
            touches.End(i % 16);
        }
    }),
    Task.Run(() =>
    {
        for (int i = 0; i < 100_000; i++)
        {
            var snapshot = touches.GetSnapshot();
            foreach (var contact in snapshot.Span)
            {
                Check(contact.Value.X == contact.Value.Y, "Concurrent reads must see complete coordinates.");
            }
        }
    }));
Check(touches.GetSnapshot().IsEmpty, "All contacts must be released after the stress test.");
Console.WriteLine("PASS: stable IDs, immutable snapshots, late moves, validation, reset, zero-allocation polling, concurrent access.");

var screen = new TestTouchScreen();
var device = new Ryujinx.HLE.Switch();
using var manager = new Ryujinx.Input.HLE.TouchScreenManager(null);
manager.Initialize(device);
screen.Buffer.Begin(10, new Vector2(100, 50));
screen.Buffer.Begin(20, new Vector2(200, 100));
Check(manager.UpdateMultiTouch(screen, true), "Active contacts must be reported.");
var frame = device.Hid.Touchscreen.Updates.Last();
Check(frame.All(point => point.Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.Start),
    "New fingers need start events.");
Check(frame[0].FingerId == 10 && frame[0].X == 200 && frame[0].Y == 100,
    "Touch IDs and scaled coordinates must reach the guest input manager.");
screen.Buffer.End(10);
screen.Buffer.Move(20, new Vector2(250, 150));
manager.UpdateMultiTouch(screen, true);
frame = device.Hid.Touchscreen.Updates.Last();
var survivor = frame.Single(point => point.FingerId == 20);
var released = frame.Single(point => point.FingerId == 10);
Check(survivor.Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.None && survivor.X == 500,
    "The remaining finger must continue with its original ID.");
Check(released.Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.End,
    "Only the released finger should receive an end event.");
int previousCount = device.Hid.Touchscreen.Updates.Count;
Check(!manager.UpdateMultiTouch(screen, false), "Unfocused input must be inactive.");
frame = device.Hid.Touchscreen.Updates[previousCount];
Check(frame.Length == 1 && frame[0].FingerId == 20 &&
    frame[0].Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.End,
    "Focus loss must release the original remaining contact.");
Check(device.Hid.Touchscreen.Updates.Last().Length == 0, "Focus loss must clear guest input.");
manager.UpdateMultiTouch(screen, true);
Check(device.Hid.Touchscreen.Updates.Last()[0].Attribute ==
    Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.Start,
    "Focus recovery must start a fresh contact.");
screen.Buffer.Clear();
Check(!manager.UpdateMultiTouch(screen, true), "Releasing all fingers must clear guest input.");
Console.WriteLine("PASS: HLE contact lifecycle, coordinate mapping, focus loss/recovery, release-all.");

for (int id = 0; id < TouchStateBuffer.MaxTouches; id++) screen.Buffer.Begin(id, Vector2.One);
screen.Buffer.Begin(100, Vector2.One);
Check(screen.GetTouches().Length == 16, "The native buffer must enforce guest touch capacity.");
manager.UpdateMultiTouch(screen, true);
screen.Buffer.Clear();
for (int id = 16; id < 32; id++) screen.Buffer.Begin(id, Vector2.One);
previousCount = device.Hid.Touchscreen.Updates.Count;
manager.UpdateMultiTouch(screen, true);
var transitions = device.Hid.Touchscreen.Updates.Skip(previousCount).ToArray();
Check(transitions.Length == 2 && transitions.All(points => points.Length == 16),
    "Replacing all fingers must preserve both transitions within guest capacity.");
Check(transitions[0].All(point => point.FingerId < 16 &&
    point.Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.End),
    "The old set must end before replacement fingers begin.");
Check(transitions[1].All(point => point.FingerId >= 16 &&
    point.Attribute == Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.TouchScreen.TouchAttribute.Start),
    "Every replacement finger must receive a start.");
Console.WriteLine("PASS: guest capacity and full contact replacement.");
