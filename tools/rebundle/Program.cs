using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rebundle;

class Program
{
    static readonly byte[] Signature = new byte[]
    {
        0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38,
        0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
        0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18,
        0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae
    };

    static int Read7BitEncodedInt(byte[] buf, ref int pos)
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = buf[pos++];
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                break;
            shift += 7;
        } while (shift < 35);
        return result;
    }

    static byte[] Write7BitEncodedInt(int value)
    {
        var result = new List<byte>();
        uint v = (uint)value;
        while (v > 0x7F)
        {
            result.Add((byte)(v | 0x80));
            v >>= 7;
        }
        result.Add((byte)v);
        return result.ToArray();
    }

    static string ReadLengthPrefixedString(byte[] buf, ref int pos)
    {
        int len = Read7BitEncodedInt(buf, ref pos);
        string s = Encoding.UTF8.GetString(buf, pos, len);
        pos += len;
        return s;
    }

    static byte[] WriteLengthPrefixedString(string s)
    {
        byte[] strBytes = Encoding.UTF8.GetBytes(s);
        var result = new List<byte>();
        result.AddRange(Write7BitEncodedInt(strBytes.Length));
        result.AddRange(strBytes);
        return result.ToArray();
    }

    static long ReadInt64(byte[] buf, int pos)
    {
        return BitConverter.ToInt64(buf, pos);
    }

    static uint ReadUInt32(byte[] buf, int pos)
    {
        return BitConverter.ToUInt32(buf, pos);
    }

    static void WriteInt64(byte[] buf, int pos, long value)
    {
        BitConverter.TryWriteBytes(buf.AsSpan(pos, 8), value);
    }

    static void WriteUInt32(byte[] buf, int pos, uint value)
    {
        BitConverter.TryWriteBytes(buf.AsSpan(pos, 4), value);
    }

    static int FindSignature(byte[] data)
    {
        int searchLimit = Math.Min(data.Length, 10 * 1024 * 1024);
        for (int i = 0; i <= searchLimit - Signature.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < Signature.Length; j++)
            {
                if (data[i + j] != Signature[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return i;
        }
        return -1;
    }

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: rebundle <input.exe> [output.exe] [relative_path=new_file_path] ...");
            Console.Error.WriteLine("       rebundle <input.exe> list");
            Environment.Exit(1);
        }

        string inputPath = args[0];
        byte[] original = File.ReadAllBytes(inputPath);
        Console.WriteLine($"Read {original.Length:N0} bytes from {inputPath}");

        int sigPos = FindSignature(original);
        if (sigPos < 0) { Console.Error.WriteLine("ERROR: Bundle signature not found."); Environment.Exit(1); }
        Console.WriteLine($"Signature found at offset 0x{sigPos:X}");

        long headerOffset = ReadInt64(original, sigPos - 8);
        Console.WriteLine($"Manifest offset: 0x{headerOffset:X}");

        int pos = (int)headerOffset;
        uint majorVersion = ReadUInt32(original, pos); pos += 4;
        uint minorVersion = ReadUInt32(original, pos); pos += 4;
        int numEmbeddedFiles = (int)ReadUInt32(original, pos); pos += 4;
        string bundleId = ReadLengthPrefixedString(original, ref pos);

        long depsJsonOffset = ReadInt64(original, pos); pos += 8;
        long depsJsonSize = ReadInt64(original, pos); pos += 8;
        long runtimeConfigJsonOffset = ReadInt64(original, pos); pos += 8;
        long runtimeConfigJsonSize = ReadInt64(original, pos); pos += 8;
        ulong flags = BitConverter.ToUInt64(original, pos); pos += 8;

        var entries = new List<BundleEntry>();
        for (int i = 0; i < numEmbeddedFiles; i++)
        {
            var entry = new BundleEntry();
            entry.Offset = ReadInt64(original, pos); pos += 8;
            entry.Size = ReadInt64(original, pos); pos += 8;
            entry.CompressedSize = ReadInt64(original, pos); pos += 8;
            entry.Type = original[pos]; pos += 1;
            entry.RelativePath = ReadLengthPrefixedString(original, ref pos);
            entries.Add(entry);
            Console.WriteLine($"  [{i}] type={entry.Type} offset=0x{entry.Offset:X} size={entry.Size} compressed={entry.CompressedSize} path={entry.RelativePath}");
        }

        if (args.Length >= 2 && string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Bundle ID: {bundleId}, Version: {majorVersion}.{minorVersion}, Files: {numEmbeddedFiles}");
            return;
        }

        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: rebundle <input.exe> <output.exe> <relative_path=new_file_path> ...");
            Console.Error.WriteLine("       rebundle <input.exe> list");
            Environment.Exit(1);
        }

        string outputPath = args[1];
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 2; i < args.Length; i++)
        {
            int eq = args[i].IndexOf('=');
            if (eq <= 0 || eq >= args[i].Length - 1)
            {
                Console.Error.WriteLine($"Invalid replacement format: {args[i]}. Expected: relative_path=new_file_path");
                Environment.Exit(1);
            }
            replacements[args[i][..eq]] = args[i][(eq + 1)..];
            Console.WriteLine($"  Replacement: {args[i][..eq]} -> {args[i][(eq + 1)..]}");
        }

        var appliedReplacements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            if (replacements.TryGetValue(entries[i].RelativePath, out string? newPath))
            {
                if (!File.Exists(newPath)) { Console.Error.WriteLine($"ERROR: Replacement file not found: {newPath}"); Environment.Exit(1); }
                byte[] newBytes = File.ReadAllBytes(newPath);
                Console.WriteLine($"  Replacing {entries[i].RelativePath} ({entries[i].Size:N0} -> {newBytes.Length:N0} bytes)");
                entries[i].ReplacementData = newBytes;
                appliedReplacements.Add(entries[i].RelativePath);
            }
        }

        foreach (var key in replacements.Keys)
        {
            if (!appliedReplacements.Contains(key))
                Console.Error.WriteLine($"WARNING: No embedded file found with path '{key}'");
        }

        long firstEmbeddedOffset = entries[0].Offset;
        Console.WriteLine($"Host PE section ends at: 0x{firstEmbeddedOffset:X}");

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        writer.Write(original, 0, (int)firstEmbeddedOffset);
        Console.WriteLine($"Wrote host PE: {firstEmbeddedOffset:N0} bytes");

        for (int i = 0; i < entries.Count; i++)
        {
            long currentPos = output.Position;
            long align = currentPos % 8;
            if (align != 0)
            {
                long pad = 8 - align;
                writer.Write(new byte[pad]);
            }

            long entryOffset = output.Position;
            entries[i].NewOffset = entryOffset;

            if (entries[i].ReplacementData is byte[] replacementData)
            {
                writer.Write(replacementData);
                entries[i].NewSize = replacementData.Length;
                entries[i].NewCompressedSize = 0;
            }
            else
            {
                writer.Write(original, (int)entries[i].Offset, (int)entries[i].Size);
                entries[i].NewSize = entries[i].Size;
                entries[i].NewCompressedSize = entries[i].CompressedSize;
            }

            Console.WriteLine($"  Entry {i}: 0x{entryOffset:X} ({entries[i].NewSize:N0} bytes)");
        }

        long newManifestOffset = output.Position;
        Console.WriteLine($"New manifest at: 0x{newManifestOffset:X}");

        byte[] header = new byte[4 + 4 + 4];
        WriteUInt32(header, 0, majorVersion);
        WriteUInt32(header, 4, minorVersion);
        WriteUInt32(header, 8, (uint)numEmbeddedFiles);
        writer.Write(header);
        writer.Write(WriteLengthPrefixedString(bundleId));

        long newDepsOffset = depsJsonOffset, newDepsSize = depsJsonSize;
        long newRtcOffset = runtimeConfigJsonOffset, newRtcSize = runtimeConfigJsonSize;
        foreach (var e in entries)
        {
            if (e.Offset == depsJsonOffset) { newDepsOffset = e.NewOffset; newDepsSize = e.NewSize; }
            if (e.Offset == runtimeConfigJsonOffset) { newRtcOffset = e.NewOffset; newRtcSize = e.NewSize; }
        }
        byte[] v2 = new byte[40];
        WriteInt64(v2, 0, newDepsOffset);
        WriteInt64(v2, 8, newDepsSize);
        WriteInt64(v2, 16, newRtcOffset);
        WriteInt64(v2, 24, newRtcSize);
        BitConverter.TryWriteBytes(v2.AsSpan(32, 8), flags);
        writer.Write(v2);

        for (int i = 0; i < entries.Count; i++)
        {
            byte[] entryBuf = new byte[8 + 8 + 8 + 1];
            WriteInt64(entryBuf, 0, entries[i].NewOffset);
            WriteInt64(entryBuf, 8, entries[i].NewSize);
            WriteInt64(entryBuf, 16, entries[i].NewCompressedSize);
            entryBuf[24] = entries[i].Type;
            writer.Write(entryBuf);
            writer.Write(WriteLengthPrefixedString(entries[i].RelativePath));
        }

        byte[] result = output.ToArray();
        Console.WriteLine($"Total output size: {result.Length:N0} bytes");

        if (sigPos - 8 >= 0 && sigPos + Signature.Length <= result.Length)
        {
            bool sigOk = true;
            for (int j = 0; j < Signature.Length; j++)
            {
                if (result[sigPos + j] != Signature[j])
                {
                    sigOk = false;
                    break;
                }
            }

            if (sigOk)
            {
                WriteInt64(result, sigPos - 8, newManifestOffset);
                Console.WriteLine($"Updated header_offset at 0x{sigPos - 8:X} -> 0x{newManifestOffset:X}");
            }
            else
            {
                int newSigPos = FindSignature(result);
                if (newSigPos >= 0)
                {
                    WriteInt64(result, newSigPos - 8, newManifestOffset);
                    Console.WriteLine($"Updated header_offset at 0x{newSigPos - 8:X} -> 0x{newManifestOffset:X}");
                }
                else
                {
                    Console.Error.WriteLine("ERROR: Could not find signature in output file.");
                    Environment.Exit(1);
                }
            }
        }

        File.WriteAllBytes(outputPath, result);
        Console.WriteLine($"Wrote rebundled output to {outputPath} ({result.Length:N0} bytes)");
    }
}

class BundleEntry
{
    public long Offset;
    public long Size;
    public long CompressedSize;
    public byte Type;
    public string RelativePath = "";

    public long NewOffset;
    public long NewSize;
    public long NewCompressedSize;
    public byte[]? ReplacementData;
}
