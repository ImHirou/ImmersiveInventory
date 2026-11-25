using System;
using System.Collections.Generic;
using System.Numerics;

namespace ImmersiveInventory.Masks;

/// <summary>
/// The silhouette of an item as the GUI draws it in a slot. Points are in GUI units (screen pixels divided by the GUI scale)
/// around the centre of the slot the item is drawn in, y down: the same coordinates the slot rendering uses, so the small
/// offset the game gives items (not blocks) is part of the silhouette.
/// </summary>
public sealed class ItemMask
{
    /// <summary> Pixels at least this opaque (0..255) belong to the item. </summary>
    public const byte DefaultAlphaThreshold = 64;

    /// <summary> Smallest squared radius of gyration, so that even a one pixel item has some inertia. </summary>
    private const float MinGyrationSquared = 1f;

    private readonly bool[] coverage;
    private readonly int[] covered;

    private ItemMask(bool[] coverage, int size, float scale)
    {
        if (size <= 0 || coverage.Length != size * size)
        {
            throw new ArgumentException("The coverage must be size × size pixels.", nameof(coverage));
        }

        this.coverage = coverage;
        Size = size;
        Scale = scale > 0f && float.IsFinite(scale) ? scale : 1f;

        var indices = new List<int>();
        double sumX = 0, sumY = 0;

        for (int i = 0; i < coverage.Length; i++)
        {
            if (!coverage[i])
            {
                continue;
            }

            indices.Add(i);
            var center = PixelCenter(i);
            sumX += center.X;
            sumY += center.Y;
        }

        covered = indices.ToArray();

        if (covered.Length == 0)
        {
            Centroid = Vector2.Zero;
            GyrationSquared = MinGyrationSquared;
            return;
        }

        Centroid = new Vector2((float)(sumX / covered.Length), (float)(sumY / covered.Length));
        double sum = 0;

        foreach (int index in covered)
        {
            sum += Vector2.DistanceSquared(PixelCenter(index), Centroid);
        }

        // A pixel is a little square, not a point: add its own second moment.
        float pixel = 1f / Scale;
        GyrationSquared = Math.Max(MinGyrationSquared, (float)(sum / covered.Length) + pixel * pixel / 6f);
    }

    /// <summary> Edge length in pixels. The slot centre is at (Size / 2, Size / 2). </summary>
    public int Size { get; }

    /// <summary> Pixels per GUI unit: the GUI scale the silhouette was drawn at. </summary>
    public float Scale { get; }

    /// <summary> Number of pixels that belong to the item. </summary>
    public int Area => covered.Length;

    public bool IsEmpty => covered.Length == 0;

    /// <summary> Centre of mass, taking every visible pixel as equally heavy. </summary>
    public Vector2 Centroid { get; }

    /// <summary> Mean squared distance of the item from its centre of mass: the moment of inertia per mass. </summary>
    public float GyrationSquared { get; }

    /// <summary> Reads the alpha of an RGBA image whose first row is the top of the slot (GUI y down). </summary>
    public static ItemMask FromRgba(byte[] rgba, int size, float scale, byte threshold = DefaultAlphaThreshold)
    {
        ArgumentNullException.ThrowIfNull(rgba);

        if (rgba.Length < size * size * 4)
        {
            throw new ArgumentException("Too few pixels.", nameof(rgba));
        }

        var coverage = new bool[size * size];

        for (int i = 0; i < coverage.Length; i++)
        {
            coverage[i] = rgba[i * 4 + 3] >= threshold;
        }

        return new ItemMask(coverage, size, scale);
    }

    /// <summary> From covered pixels, row by row from the top. </summary>
    public static ItemMask FromCoverage(bool[] coverage, int size, float scale) => new((bool[])coverage.Clone(), size, scale);

    /// <summary> A round stand-in, for items whose silhouette could not be drawn. </summary>
    public static ItemMask Disc(float radius, float scale)
    {
        scale = scale > 0f && float.IsFinite(scale) ? scale : 1f;
        int size = Math.Max(2, (int)MathF.Ceiling(radius * scale) * 2 + 2);
        size += size % 2;
        var coverage = new bool[size * size];
        float limit = radius * scale;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - size / 2f;
                float dy = y + 0.5f - size / 2f;
                coverage[y * size + x] = dx * dx + dy * dy <= limit * limit;
            }
        }

        return new ItemMask(coverage, size, scale);
    }

    public bool Covers(Vector2 point)
    {
        int x = (int)MathF.Floor(point.X * Scale + Size / 2f);
        int y = (int)MathF.Floor(point.Y * Scale + Size / 2f);
        return x >= 0 && y >= 0 && x < Size && y < Size && coverage[y * Size + x];
    }

    /// <summary> <paramref name="point"/> itself if it is on the item, otherwise the centre of the item's nearest pixel. </summary>
    public Vector2 Nearest(Vector2 point)
    {
        if (covered.Length == 0 || Covers(point))
        {
            return point;
        }

        var best = point;
        float bestDistance = float.PositiveInfinity;

        foreach (int index in covered)
        {
            var center = PixelCenter(index);
            float distance = Vector2.DistanceSquared(center, point);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = center;
            }
        }

        return best;
    }

    /// <summary> The centre of the item's pixel farthest from <paramref name="from"/>: the tip of a blade held at its hilt. </summary>
    public Vector2 Farthest(Vector2 from)
    {
        var best = from;
        float bestDistance = -1f;

        foreach (int index in covered)
        {
            var center = PixelCenter(index);
            float distance = Vector2.DistanceSquared(center, from);

            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = center;
            }
        }

        return best;
    }

    private Vector2 PixelCenter(int index)
    {
        int x = index % Size;
        int y = index / Size;
        return new Vector2((x + 0.5f - Size / 2f) / Scale, (y + 0.5f - Size / 2f) / Scale);
    }
}
