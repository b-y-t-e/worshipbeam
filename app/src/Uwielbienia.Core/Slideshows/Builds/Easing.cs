// Port DeckCraft: crates/anim/src/easing.rs — patrz UPSTREAM.md.
namespace Uwielbienia.Core.Slideshows.Builds;

/// <summary>Przebieg czasu efektu: przyspieszanie / zwalnianie i odbicie na końcu.</summary>
public static class Easing
{
    /// <summary>
    /// Postęp liniowy <paramref name="p"/> (0..1) po wygładzeniu jak w PowerPoincie: <paramref name="accel"/> i
    /// <paramref name="decel"/> to części czasu na rozpędzanie i hamowanie (prędkość rośnie liniowo),
    /// <paramref name="bounce"/> (0..1) — końcówka odbija się od celu.
    /// </summary>
    public static double Ease(double p, double accel, double decel, double bounce)
    {
        p = Rs.Clampf(p, 0.0, 1.0, 0.0);
        var a = Rs.Clampf(accel, 0.0, 1.0, 0.0);
        var d = Rs.Clampf(decel, 0.0, 1.0, 0.0);
        if (a + d > 1.0)
        {
            var s = a + d;
            a /= s;
            d /= s;
        }
        var v = 2.0 / (2.0 - a - d);
        double y;
        if (a > 0.0 && p < a)
            y = v * p * p / (2.0 * a);
        else if (d > 0.0 && p > 1.0 - d)
        {
            var q = 1.0 - p;
            y = 1.0 - v * q * q / (2.0 * d);
        }
        else
            y = v * a / 2.0 + v * (p - a);
        var b = Rs.Clampf(bounce, 0.0, 1.0, 0.0);
        if (b > 0.0)
        {
            // cel osiągnięty wcześniej, potem coraz niższe odbicia w ostatniej części `b`
            var hit = 1.0 - b;
            if (p < hit)
            {
                var q = hit > 0.0 ? p / hit : 1.0;
                y = q * q;
            }
            else
            {
                var s = (p - hit) / b;
                y = 1.0 - BounceHops(s) * 0.25;
            }
        }
        return Rs.Clampf(y, -1.0, 2.0, 0.0);
    }

    /// <summary>Malejące odbicia dla s w 0..1: zero na końcach każdego odbicia, coraz niższe szczyty.</summary>
    private static double BounceHops(double s)
    {
        s = Rs.Clampf(s, 0.0, 1.0, 1.0);
        const double hops = 3.0;
        var k = Rs.Min(Math.Floor(s * hops), hops - 1.0);
        var local = s * hops - k;
        var amp = Math.Pow(0.5, k);
        return amp * Math.Sin(local * Math.PI);
    }

    /// <summary>Klasyczne „bounce-out”: spada do 1 i odbija się coraz niżej.</summary>
    internal static double BounceOut(double p)
    {
        p = Rs.Clampf(p, 0.0, 1.0, 0.0);
        const double n = 7.5625;
        const double d = 2.75;
        double y;
        if (p < 1.0 / d)
            y = n * p * p;
        else if (p < 2.0 / d)
        {
            var q = p - 1.5 / d;
            y = n * q * q + 0.75;
        }
        else if (p < 2.5 / d)
        {
            var q = p - 2.25 / d;
            y = n * q * q + 0.9375;
        }
        else
        {
            var q = p - 2.625 / d;
            y = n * q * q + 0.984375;
        }
        return Rs.Min(y, 1.0);
    }

    /// <summary>Gładkie przyspieszenie i zwolnienie (sześcienny smoothstep).</summary>
    internal static double Smooth(double p)
    {
        p = Rs.Clampf(p, 0.0, 1.0, 0.0);
        return p * p * (3.0 - 2.0 * p);
    }
}
