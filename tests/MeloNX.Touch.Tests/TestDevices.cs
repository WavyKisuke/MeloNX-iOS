using System.Drawing;
using System.Numerics;
using Ryujinx.HLE.HOS.Services.Hid;
using Ryujinx.Input;

// Only the emulator/device boundaries are replaced. The touch interface, coordinate
// conversion, input buffer, and HLE touch manager are compiled from production sources.
namespace Ryujinx.Input
{
    public interface IGamepad { }
}

namespace Ryujinx.HLE
{
    public sealed class Switch
    {
        public TestHid Hid { get; } = new();
    }

    public sealed class TestHid
    {
        public RecordingTouchDevice Touchscreen { get; } = new();
    }

    public sealed class RecordingTouchDevice
    {
        public List<TouchPoint[]> Updates { get; } = new();
        public void Update(params TouchPoint[] points) => Updates.Add(points);
    }
}

public sealed class TestTouchScreen : ITouchScreen
{
    public TouchStateBuffer Buffer { get; } = new();
    public Size ClientSize => new(640, 360);
    public ReadOnlyMemory<KeyValuePair<int, Vector2>> GetTouches() => Buffer.GetSnapshot();
}
