// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using Shouldly;

namespace SilexGis.Api.Tests.Support;

/// <summary>
/// A mesh file of a render bundle, read back by a test: a binary PLY as the bundle's writer makes
/// them. Each vertex's place, and its colour as six hex digits where the file gives vertices one.
/// </summary>
public static class BundlePly
{
    public static (List<double[]> Vertices, List<string>? Colours) Read(byte[] file)
    {
        var end = Encoding.ASCII.GetBytes("end_header\n");
        var at = file.AsSpan().IndexOf(end) + end.Length;
        var header = Encoding.ASCII.GetString(file, 0, at).Split('\n');
        header.ShouldContain("format binary_little_endian 1.0");
        var count = int.Parse(
            header.Single(l => l.StartsWith("element vertex ", StringComparison.Ordinal))[15..], CultureInfo.InvariantCulture);

        // What a vertex is made of, in the order the file says, up to the next element.
        var properties = header
            .SkipWhile(l => !l.StartsWith("element vertex ", StringComparison.Ordinal)).Skip(1)
            .TakeWhile(l => l.StartsWith("property ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .ToList();
        properties.Take(3).Select(p => p[2]).ShouldBe(["x", "y", "z"]);
        properties.Take(3).ShouldAllBe(p => p[1] == "float");
        var coloured = properties.Any(p => p[2] == "red");
        var stride = properties.Sum(Size);
        var red = properties.TakeWhile(p => p[2] != "red").Sum(Size);

        var vertices = new List<double[]>(count);
        var colours = coloured ? new List<string>(count) : null;
        for (var v = 0; v < count; v++)
        {
            var from = at + (v * stride);
            vertices.Add([BitConverter.ToSingle(file, from), BitConverter.ToSingle(file, from + 4), BitConverter.ToSingle(file, from + 8)]);
            colours?.Add(Convert.ToHexStringLower(file, from + red, 3));
        }

        return (vertices, colours);
    }

    /// <summary>
    /// The number each vertex of the file carries for a reveal — the float property <c>reveal</c> —
    /// or null for a file whose vertices carry none.
    /// </summary>
    public static List<double>? Reveal(byte[] file)
    {
        var end = Encoding.ASCII.GetBytes("end_header\n");
        var at = file.AsSpan().IndexOf(end) + end.Length;
        var header = Encoding.ASCII.GetString(file, 0, at).Split('\n');
        var count = int.Parse(
            header.Single(l => l.StartsWith("element vertex ", StringComparison.Ordinal))[15..], CultureInfo.InvariantCulture);
        var properties = header
            .SkipWhile(l => !l.StartsWith("element vertex ", StringComparison.Ordinal)).Skip(1)
            .TakeWhile(l => l.StartsWith("property ", StringComparison.Ordinal))
            .Select(l => l.Split(' '))
            .ToList();
        if (!properties.Any(p => p[2] == "reveal"))
        {
            return null;
        }

        properties.Single(p => p[2] == "reveal")[1].ShouldBe("float");
        var stride = properties.Sum(Size);
        var before = properties.TakeWhile(p => p[2] != "reveal").Sum(Size);
        var values = new List<double>(count);
        for (var v = 0; v < count; v++)
        {
            values.Add(BitConverter.ToSingle(file, at + (v * stride) + before));
        }

        return values;
    }

    private static int Size(string[] property) => property[1] == "uchar" ? 1 : 4;
}
