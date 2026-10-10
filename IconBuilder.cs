using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

static class IconBuilder
{
    internal static Bitmap Render(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        double center = (size - 1) / 2.0, radius = size / 2.0 - 1;
        int edge = Math.Max(1, size / 24);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double distance = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                if (distance <= radius) bitmap.SetPixel(x, y, distance > radius - edge ? Color.Black : Color.White);
            }
        using (var g = Graphics.FromImage(bitmap)) PixelB.Draw(g, new Rectangle(0, 0, size, size), Color.Black);
        return bitmap;
    }

    static void Main(string[] args)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
            using (var bitmap = Render(sizes[i]))
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png); images[i] = memory.ToArray();
            }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(args[0], "MarkdownBold.ico"))))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
    }
}
