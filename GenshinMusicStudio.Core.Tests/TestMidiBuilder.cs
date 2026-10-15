using System.Text;

namespace GenshinMusicStudio.Core.Tests;

/// <summary>Builds compact synthetic MIDI files without depending on an external library.</summary>
internal static class TestMidiBuilder
{
    /// <summary>Creates a one-track MIDI file from raw track bytes.</summary>
    public static byte[] CreateSingleTrack(byte[] trackData, int division = 480)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(new byte[] { 0, 0, 0, 6, 0, 0, 0, 1, (byte)(division >> 8), (byte)division });
        bytes.AddRange(Encoding.ASCII.GetBytes("MTrk"));
        bytes.AddRange(new byte[]
        {
            (byte)(trackData.Length >> 24),
            (byte)(trackData.Length >> 16),
            (byte)(trackData.Length >> 8),
            (byte)trackData.Length,
        });
        bytes.AddRange(trackData);
        return bytes.ToArray();
    }

    /// <summary>Creates a two-track file used to verify track iteration.</summary>
    public static byte[] CreateTwoTracks(byte[] firstTrack, byte[] secondTrack)
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("MThd"));
        bytes.AddRange(new byte[] { 0, 0, 0, 6, 0, 0, 0, 2, 0x01, 0xE0 });
        AppendTrack(bytes, firstTrack);
        AppendTrack(bytes, secondTrack);
        return bytes.ToArray();
    }

    /// <summary>Encodes a MIDI variable-length quantity.</summary>
    public static byte[] VarLen(int value)
    {
        var buffer = new List<byte> { (byte)(value & 0x7F) };
        value >>= 7;
        while (value > 0)
        {
            buffer.Insert(0, (byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        return buffer.ToArray();
    }

    private static void AppendTrack(List<byte> target, byte[] trackData)
    {
        target.AddRange(Encoding.ASCII.GetBytes("MTrk"));
        target.AddRange(new byte[]
        {
            (byte)(trackData.Length >> 24),
            (byte)(trackData.Length >> 16),
            (byte)(trackData.Length >> 8),
            (byte)trackData.Length,
        });
        target.AddRange(trackData);
    }
}
