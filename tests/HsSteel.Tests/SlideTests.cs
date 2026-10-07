using HsSteel.Assets.Slides;

namespace HsSteel.Tests;

public class SlideTests
{
    private static byte[] Header(int level = 2, int sx = 100, int sy = 80)
    {
        var b = new List<byte>();
        b.AddRange("AutoCAD Slide\r\n\u001a\0"u8.ToArray());
        b.Add(86);
        b.Add((byte)level);
        b.AddRange(BitConverter.GetBytes((ushort)sx));
        b.AddRange(BitConverter.GetBytes((ushort)sy));
        b.AddRange(BitConverter.GetBytes(1_000_000u));
        b.AddRange(BitConverter.GetBytes((ushort)2));
        b.AddRange(BitConverter.GetBytes((ushort)0x1234));
        return b.ToArray();
    }

    private static IEnumerable<byte> W(params int[] words) => words.SelectMany(w => BitConverter.GetBytes((short)w));

    private static IEnumerable<byte> Color(int c) => [(byte)c, 0xFF];

    private static IEnumerable<byte> Solid(int x1, int y1, int x2, int y2, int x3, int y3) =>
        new byte[] { 0, 0xFD }.Concat(W(4, -2))
            .Concat(new byte[] { 0, 0xFD }).Concat(W(x1, y1))
            .Concat(new byte[] { 0, 0xFD }).Concat(W(x2, y2))
            .Concat(new byte[] { 0, 0xFD }).Concat(W(x3, -y3));

    private static readonly byte[] Eof = [0, 0xFC];

    [Fact]
    public void ParsesVectorsColorsAndSolids()
    {
        var data = Header().Concat(Color(5)).Concat(W(1, 2, 30, 40)).Concat(Color(2)).Concat(W(30, 40, 50, 10)).Concat(Solid(10, 10, 20, 10, 15, 20)).Concat(Eof).ToArray();
        var s = SlideReader.Parse(data);
        Assert.Equal(2, s.Level);
        Assert.Equal((100, 80), (s.Width, s.Height));
        Assert.Equal(2, s.VectorCount);
        Assert.Equal(1, s.SolidCount);
        Assert.Equal(2, s.ColorChanges);
        Assert.Equal(new[] { (10, 10), (20, 10), (15, 20) }, s.Shapes[2].Points);
        Assert.Equal(2, s.Shapes[2].Color);
        var svg = SlideReader.ToSvg(s);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("stroke=\"#0000ff\"", svg);
        Assert.Contains("fill=\"#ffff00\"", svg);
        Assert.Equal(svg, SlideReader.ToSvg(SlideReader.Parse(data)));
    }

    [Fact]
    public void QuadSolidUsesAutoCadVertexOrder()
    {
        var data = Header().Concat(new byte[] { 0, 0xFD }).Concat(W(4, -2))
            .Concat(new byte[] { 0, 0xFD }).Concat(W(0, 0)).Concat(new byte[] { 0, 0xFD }).Concat(W(10, 0)).Concat(new byte[] { 0, 0xFD }).Concat(W(0, 10))
            .Concat(new byte[] { 0, 0xFD }).Concat(W(10, -10)).Concat(Eof).ToArray();
        var s = SlideReader.Parse(data);
        Assert.Equal(new[] { (0, 0), (10, 0), (10, 10), (0, 10) }, s.Shapes[0].Points);
    }

    [Fact]
    public void OffsetAndCommonEndPointVectorsInLevel2()
    {
        var data = Header().Concat(W(10, 10, 20, 20))
            .Concat(new byte[] { 5, 0xFB, 3, 7, 4 })      // start +5,+3 from prev start; end +7,+4 from prev end
            .Concat(new byte[] { 0xFF, 0xFE, 2 })          // common end point: from prev end, +(-1,+2)
            .Concat(Eof).ToArray();
        var s = SlideReader.Parse(data);
        Assert.Equal(new[] { (15, 13), (27, 24) }, s.Shapes[1].Points);
        Assert.Equal(new[] { (27, 24), (26, 26) }, s.Shapes[2].Points);
    }

    [Fact]
    public void OldFormatRejectsOffsetVectorsButReadsLongVectors()
    {
        var ok = Header(level: 1).Concat(W(1, 1, 2, 2)).Concat(Eof).ToArray();
        Assert.Equal(1, SlideReader.Parse(ok).Level);
        var bad = Header(level: 1).Concat(W(1, 1, 2, 2)).Concat(new byte[] { 1, 0xFB, 1, 1, 1 }).Concat(Eof).ToArray();
        Assert.Throws<InvalidDataException>(() => SlideReader.Parse(bad));
    }

    [Fact]
    public void RejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => SlideReader.Parse(new byte[40]));
        Assert.Throws<InvalidDataException>(() => SlideReader.Parse(Header().Concat(W(1, 2, 3)).ToArray())); // truncated record
        Assert.Throws<InvalidDataException>(() => SlideReader.Parse(Header().Concat(W(1, 2, 3, 4)).ToArray())); // missing end record
    }

    [Fact]
    public void AciPalette()
    {
        Assert.Equal("#ff0000", SlideReader.AciHex(1));
        Assert.Equal("#ffffff", SlideReader.AciHex(7));
        Assert.Equal("#ff0000", SlideReader.AciHex(10));
        Assert.Equal("#ffffff", SlideReader.AciHex(255));
    }

    [Fact]
    public void WholeLegacyIconDirParses()
    {
        var dir = @"C:\HS-STEEL\HSSTEEL\Icons";
        if (!Directory.Exists(dir))
        {
            return;
        }

        var files = Directory.GetFiles(dir, "*.sld");
        Assert.NotEmpty(files);
        foreach (var f in files)
        {
            var s = SlideReader.Parse(File.ReadAllBytes(f));
            Assert.True(s.Width > 0 && s.Height > 0, f);
            Assert.NotEmpty(s.Shapes);
        }

        var b1 = SlideReader.Parse(File.ReadAllBytes(Path.Combine(dir, "BRACE1.sld")));
        Assert.Equal((272, 270), (b1.Width, b1.Height));
        Assert.True(b1.SolidCount > 0 && b1.VectorCount > 0);
    }
}
