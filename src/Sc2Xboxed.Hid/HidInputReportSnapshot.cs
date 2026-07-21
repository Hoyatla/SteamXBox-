namespace Sc2Xboxed.Hid;

public sealed record HidInputReportSnapshot(DateTimeOffset Timestamp, byte[] Data)
{
    public string Hex => Convert.ToHexString(Data);
}
