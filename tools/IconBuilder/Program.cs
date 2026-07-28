using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: IconBuilder <source.png> <output.ico>");
    return 1;
}

var sourcePath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
var frames = new List<(int Size, byte[] Data)>();

using (var source = new Bitmap(sourcePath))
{
    foreach (var size in sizes)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(
                source,
                new Rectangle(0, 0, size, size),
                new Rectangle(0, 0, source.Width, source.Height),
                GraphicsUnit.Pixel);
        }

        frames.Add((size, BuildIconDib(bitmap)));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var output = File.Create(outputPath))
using (var writer = new BinaryWriter(output))
{
    writer.Write((ushort)0);
    writer.Write((ushort)1);
    writer.Write((ushort)frames.Count);

    var dataOffset = 6 + (frames.Count * 16);
    foreach (var frame in frames)
    {
        writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
        writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write((uint)frame.Data.Length);
        writer.Write((uint)dataOffset);
        dataOffset += frame.Data.Length;
    }

    foreach (var frame in frames)
    {
        writer.Write(frame.Data);
    }
}

Console.WriteLine($"Created {outputPath} with {frames.Count} sizes.");
return 0;

static byte[] BuildIconDib(Bitmap bitmap)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    var width = bitmap.Width;
    var height = bitmap.Height;
    var pixelBytes = width * height * 4;
    var maskStride = ((width + 31) / 32) * 4;

    writer.Write((uint)40);
    writer.Write(width);
    writer.Write(height * 2);
    writer.Write((ushort)1);
    writer.Write((ushort)32);
    writer.Write((uint)0);
    writer.Write((uint)pixelBytes);
    writer.Write(0);
    writer.Write(0);
    writer.Write((uint)0);
    writer.Write((uint)0);

    for (var y = height - 1; y >= 0; y--)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            writer.Write(pixel.B);
            writer.Write(pixel.G);
            writer.Write(pixel.R);
            writer.Write(pixel.A);
        }
    }

    writer.Write(new byte[maskStride * height]);
    return stream.ToArray();
}
