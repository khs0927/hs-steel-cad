using System.Globalization;
using HsSteel.Domain;

namespace HsSteel.Modeling;

/// <summary>
/// Default bolt-row layout for a single-plate shear tab when the splice standard has no entry for the beam.
/// </summary>
/// <remarks>
/// <para>Rule (engineering assumption, documented in docs/SHEAR-TAB-ROWS.md):</para>
/// <list type="bullet">
/// <item>Rows must fit inside the <b>clear web depth</b> T = D − 2·(tf + r): the plate must not run into the
/// flanges or the root fillets.</item>
/// <item>Vertical edge distance e ≥ minimum edge distance for a <b>sheared</b> edge, KDS 14 31 25 (KBC 2016
/// 0710.3.4) = AISC 360-05 Table J3.4M: M16 28, M20 34, M22 38, M24 42, M27 48, M30 52 mm, and never less
/// than the shop default of 40 mm.</item>
/// <item>Pitch p ≥ 3d (preferred spacing, KDS 14 31 25 / AISC 360 J3.3; the absolute minimum is 2⅔d), and
/// never less than the shop default of 70 mm. Rounded up to 5 mm.</item>
/// <item>n = ⌊(T − 2e) / p⌋ + 1 rows; plate height = 2e + (n − 1)·p ≤ T; centred on the beam depth.</item>
/// <item>Fewer than 2 rows is outside the conventional single-plate configuration (AISC Steel Construction
/// Manual Part 10, n = 2…12) and is flagged; if not even one row fits with the edge distances, a single
/// row is still drawn (centred, plate = T) so the model stays complete, and the connection is flagged for
/// the engineer (use an end plate, angles or a deeper section).</item>
/// </list>
/// <para>This is a detailing default, not a strength check: bolt shear, bearing and block shear are not
/// verified here.</para>
/// </remarks>
public static class ShearTabLayout
{
    public const double ShopEdge = 40;
    public const double ShopPitch = 70;

    /// <summary>Minimum edge distance (mm) to a sheared edge, KDS 14 31 25 / AISC 360-05 Table J3.4M.</summary>
    public static double MinEdgeSheared(double boltDia) => boltDia switch
    {
        <= 16 => 28,
        <= 20 => 34,
        <= 22 => 38,
        <= 24 => 42,
        <= 27 => 48,
        <= 30 => 52,
        _ => Math.Ceiling(1.75 * boltDia),   // > M30: 1.75d (AISC 360-05 Table J3.4M footnote)
    };

    /// <summary>Clear web depth T = D − 2(tf + r); r is 0 when the section table has no fillet radius.</summary>
    public static double ClearWebDepth(Profile p) => p.Depth - (2 * (p.Tf + p.Radius));

    public sealed record Layout(BoltAxis Rows, double Edge, double Pitch, double ClearWeb, IReadOnlyList<string> Flags)
    {
        public int Count => Rows.Count;
    }

    public static Layout Default(Profile p, double boltDia)
    {
        var clear = ClearWebDepth(p);
        var e = Math.Max(ShopEdge, MinEdgeSheared(boltDia));
        var pitch = Math.Max(ShopPitch, Math.Ceiling(3 * boltDia / 5) * 5);
        var flags = new List<string>();
        var n = clear >= 2 * e ? (int)Math.Floor(((clear - (2 * e)) / pitch) + 1e-9) + 1 : 0;
        string axis;
        if (n >= 1)
        {
            axis = n == 1 ? F($"{e}+0A{pitch}+{e}") : F($"{e}+{n - 1}A{pitch}+{e}");
        }
        else
        {
            var half = Math.Max(0, clear) / 2;
            axis = F($"{half}+0A{pitch}+{half}");
            flags.Add(F($"clear web {clear:0.#} mm < 2 x edge distance {e:0} mm (M{boltDia:0}, KDS 14 31 25 / AISC 360 Table J3.4M): one bolt row does not satisfy the minimum edge distance"));
        }

        if (n < 2)
        {
            flags.Add(F($"only {Math.Max(n, 1)} bolt row fits in clear web {clear:0.#} mm (conventional single-plate shear tabs use 2-12 rows, AISC Manual Part 10); use an end plate / double angles or check the connection"));
        }

        return new Layout(BoltAxis.Parse(axis), e, pitch, clear, flags);
    }

    /// <summary>Flags for an axis taken from the splice standard that does not fit the clear web.</summary>
    public static IReadOnlyList<string> Check(Profile p, BoltAxis rows)
    {
        var clear = ClearWebDepth(p);
        return rows.Total > clear + 1e-6
            ? [F($"shear-tab plate height {rows.Total:0.#} mm exceeds clear web depth {clear:0.#} mm (D - 2(tf + r))")]
            : [];
    }

    private static string F(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
