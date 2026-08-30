namespace TahoeCursorStudio.Services;

public static class CursorFormatInspector
{
    public static IReadOnlyList<int> ReadCurSizes(string path)
    {
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 2)
            throw new InvalidDataException($"不是有效的 CUR 文件：{path}");
        var count = reader.ReadUInt16();
        var sizes = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            _ = height;
            reader.BaseStream.Position += 14;
        }
        return sizes;
    }
}
