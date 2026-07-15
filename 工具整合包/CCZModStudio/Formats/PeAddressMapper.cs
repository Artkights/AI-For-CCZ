namespace CCZModStudio.Formats;

public sealed class PeAddressMapper
{
    private readonly List<Section> _sections = new();
    public uint ImageBase { get; private set; }

    public static PeAddressMapper Load(string exePath)
    {
        var mapper = new PeAddressMapper();
        mapper.Read(exePath);
        return mapper;
    }

    public long VirtualAddressToFileOffset(uint virtualAddress)
    {
        if (virtualAddress < ImageBase)
        {
            throw new InvalidOperationException($"虚拟地址 0x{virtualAddress:X} 小于 ImageBase 0x{ImageBase:X}。 ");
        }

        var rva = virtualAddress - ImageBase;
        foreach (var section in _sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return section.RawPointer + (rva - section.VirtualAddress);
            }
        }

        throw new InvalidOperationException($"无法将虚拟地址 0x{virtualAddress:X} 映射到文件偏移。 ");
    }

    public uint FileOffsetToVirtualAddress(long fileOffset)
    {
        if (fileOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileOffset));
        }

        foreach (var section in _sections)
        {
            if (fileOffset >= section.RawPointer && fileOffset < section.RawPointer + section.RawSize)
            {
                var delta = checked((uint)(fileOffset - section.RawPointer));
                return checked(ImageBase + section.VirtualAddress + delta);
            }
        }

        throw new InvalidOperationException($"无法将文件偏移 0x{fileOffset:X} 映射到虚拟地址。 ");
    }

    private void Read(string exePath)
    {
        using var stream = File.OpenRead(exePath);
        using var reader = new BinaryReader(stream);
        stream.Position = 0x3C;
        var peOffset = reader.ReadInt32();
        stream.Position = peOffset;
        var signature = reader.ReadUInt32();
        if (signature != 0x00004550) throw new InvalidOperationException("不是有效 PE 文件。 ");

        var machine = reader.ReadUInt16();
        var sectionCount = reader.ReadUInt16();
        stream.Position += 12;
        var optionalHeaderSize = reader.ReadUInt16();
        stream.Position += 2;

        var optionalHeaderStart = stream.Position;
        var magic = reader.ReadUInt16();
        if (magic == 0x10B)
        {
            stream.Position = optionalHeaderStart + 28;
            ImageBase = reader.ReadUInt32();
        }
        else if (magic == 0x20B)
        {
            stream.Position = optionalHeaderStart + 24;
            ImageBase = checked((uint)reader.ReadUInt64());
        }
        else
        {
            throw new InvalidOperationException($"未知 PE OptionalHeader magic：0x{magic:X}");
        }

        stream.Position = optionalHeaderStart + optionalHeaderSize;
        _sections.Clear();
        for (var i = 0; i < sectionCount; i++)
        {
            var nameBytes = reader.ReadBytes(8);
            var name = System.Text.Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');
            var virtualSize = reader.ReadUInt32();
            var virtualAddress = reader.ReadUInt32();
            var rawSize = reader.ReadUInt32();
            var rawPointer = reader.ReadUInt32();
            stream.Position += 16;
            _sections.Add(new Section(name, virtualAddress, virtualSize, rawPointer, rawSize));
        }
    }

    private sealed record Section(string Name, uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize);
}
