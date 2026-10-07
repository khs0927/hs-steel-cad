using System.Globalization;
using System.Text;

namespace HsSteel.Assets.Blocks;

public static class BlockPreview
{
    /// <summary>Black-on-white SVG of the block's line work (Y flipped to screen space).</summary>
    public static string RenderSvg(string dwgPath, int size = 128)
    {
        var doc = BlockCatalog.ReadDoc(dwgPath);
        var ents = doc.ModelSpace.Entities.ToList();
        var segs = BlockGeometry.Segments(ents, 0).ToList();
        var ext = new Extents2();
        foreach (var (a, b) in segs) { ext.Add(a.X, a.Y); ext.Add(b.X, b.Y); }
        foreach (var p in BlockGeometry.Points(ents, 0)) ext.Add(p.X, p.Y);
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.Append(ci, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\">");
        sb.Append("<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><g stroke=\"#000\" stroke-width=\"1\" fill=\"none\">");
        if (!ext.IsEmpty)
        {
            double w = Math.Max(ext.MaxX - ext.MinX, 1e-9), h = Math.Max(ext.MaxY - ext.MinY, 1e-9);
            double pad = size * 0.06, k = (size - 2 * pad) / Math.Max(w, h);
            double ox = pad + ((size - 2 * pad) - w * k) / 2, oy = pad + ((size - 2 * pad) - h * k) / 2;
            string X(double x) => ((x - ext.MinX) * k + ox).ToString("0.##", ci);
            string Y(double y) => (size - ((y - ext.MinY) * k + oy)).ToString("0.##", ci);
            foreach (var (a, b) in segs)
                sb.Append(ci, $"<line x1=\"{X(a.X)}\" y1=\"{Y(a.Y)}\" x2=\"{X(b.X)}\" y2=\"{Y(b.Y)}\"/>");
        }
        sb.Append("</g></svg>");
        return sb.ToString();
    }

    public static string RenderSvg(BlockAsset asset, int size = 128) => RenderSvg(asset.SourcePath, size);
}
