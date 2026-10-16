using System;
using System.Globalization;

namespace Brisk.Session;

/// Sizes as the window prints them: GB with one decimal under ten and whole
/// from ten up, MB whole, in the window's language (Turkish "5,1").
public static class Sizes
{
    private const double Gb = 1L << 30, Mb = 1L << 20;

    public static (string Number, string Unit) Split(long bytes, CultureInfo c)
    {
        if (bytes <= 0) return ("0", "GB");
        if (bytes >= 1L << 30)
        {
            var g = Math.Round(bytes / Gb, 1, MidpointRounding.AwayFromZero);
            return (g < 10 ? g.ToString("0.0", c) : Math.Round(g, MidpointRounding.AwayFromZero).ToString("0", c), "GB");
        }
        if (bytes >= 1L << 20) return (Math.Round(bytes / Mb, MidpointRounding.AwayFromZero).ToString("0", c), "MB");
        return ("<1", "MB");
    }

    public static string Format(long bytes, CultureInfo c)
    {
        var (n, u) = Split(bytes, c);
        return $"{n} {u}";
    }
}
