namespace Virtua.Display;

internal enum DriverStatusKind
{
    Ready = 0,
    Missing = 2,
    Incompatible = 3,
    Error = 4
}

internal readonly record struct DriverStatus(DriverStatusKind Kind, string Message)
{
    internal const byte SupportedMajor = 0;
    internal const byte RequiredMinor = 2;

    internal static DriverStatus FromProtocol(byte major, byte minor, byte incremental) =>
        major == SupportedMajor && minor >= RequiredMinor
            ? new(DriverStatusKind.Ready,
                $"SudoVDA protocol {major}.{minor}.{incremental} is ready.")
            : new(DriverStatusKind.Incompatible,
                $"SudoVDA protocol {major}.{minor}.{incremental} is incompatible; need " +
                $"{SupportedMajor}.{RequiredMinor} or newer minor version.");
}
