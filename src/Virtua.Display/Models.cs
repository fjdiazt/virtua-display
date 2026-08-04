using System.Drawing;

namespace Virtua.Display;

internal readonly record struct DisplayMode(uint Width, uint Height, uint RefreshHz)
{
    public override string ToString() => $"{Width} x {Height} @ {RefreshHz} Hz";
}

internal sealed record DisplayState(
    string DeviceName,
    Point Position,
    DisplayMode Mode,
    bool Primary);

internal sealed record DisplaySnapshot(IReadOnlyList<DisplayState> Displays);

internal readonly record struct AddedDisplay(long AdapterLuid, uint TargetId);
