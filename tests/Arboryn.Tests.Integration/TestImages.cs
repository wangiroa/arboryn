namespace Arboryn.Tests.Integration;

/// <summary>
/// Génère des images BMP 24 bits déterministes pour les tests perceptuels (décodées par le
/// vrai hasher CoenM/ImageSharp, indépendamment de l'extension).
/// </summary>
internal static class TestImages
{
    /// <summary>Dégradé lisse basse fréquence — empreinte perceptuelle stable.</summary>
    public static byte[] Gradient(int width = 64, int height = 64)
        => Bmp24(width, height, (x, y) => ((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2)));

    /// <summary>Même dégradé à résolution réduite (blocs 2×2) — analogue d'une recompression.</summary>
    public static byte[] GradientReduced(int width = 64, int height = 64)
        => Bmp24(width, height, (x, y) => ((byte)((x & ~1) * 4), (byte)((y & ~1) * 4), (byte)(((x & ~1) + (y & ~1)) * 2)));

    /// <summary>Damier — contenu très différent du dégradé.</summary>
    public static byte[] Checkerboard(int width = 64, int height = 64)
        => Bmp24(width, height, (x, y) => ((x / 8) + (y / 8)) % 2 == 0 ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255));

    public static byte[] Bmp24(int width, int height, Func<int, int, (byte R, byte G, byte B)> pixel)
    {
        var rowSize = ((width * 3) + 3) / 4 * 4;
        var imageSize = rowSize * height;
        const int headerSize = 54;
        var bytes = new byte[headerSize + imageSize];

        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, headerSize + imageSize);
        WriteInt32(bytes, 10, headerSize);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        bytes[26] = 1;
        bytes[28] = 24;
        WriteInt32(bytes, 38, 2835);
        WriteInt32(bytes, 42, 2835);

        for (var fileRow = 0; fileRow < height; fileRow++)
        {
            var y = height - 1 - fileRow;
            var rowStart = headerSize + (fileRow * rowSize);
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = pixel(x, y);
                var p = rowStart + (x * 3);
                bytes[p] = b;
                bytes[p + 1] = g;
                bytes[p + 2] = r;
            }
        }

        return bytes;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
