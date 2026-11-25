using System;
using System.Numerics;

namespace ImmersiveInventory.Rules;

/// <summary> Nearest points on <see cref="GripZone"/>s. Results are in GUI units around the slot centre, y down. </summary>
public static class GripGeometry
{
    /// <summary> Edge of a slot in GUI units: 1.0 in effect units. </summary>
    public const float SlotSize = 48f;

    /// <summary>
    /// The point of the zones nearest to <paramref name="point"/>. <paramref name="itemCenter"/> is where the item's centre is drawn
    /// relative to the slot centre (items are drawn 3 left and 1 up). Without usable zones, <paramref name="point"/> itself.
    /// </summary>
    public static Vector2 Nearest(GripZone[]? zones, Vector2 point, Vector2 itemCenter)
    {
        var best = point;
        float bestDistance = float.PositiveInfinity;

        foreach (var zone in zones ?? Array.Empty<GripZone>())
        {
            if (zone == null || !TryNearest(zone, point, itemCenter, out var candidate))
            {
                continue;
            }

            float distance = Vector2.DistanceSquared(candidate, point);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static bool TryNearest(GripZone zone, Vector2 point, Vector2 itemCenter, out Vector2 nearest)
    {
        nearest = default;

        if (zone.Point is { Length: >= 2 } p)
        {
            nearest = ToGui(p[0], p[1], itemCenter);
            return IsFinite(nearest);
        }

        if (zone.Line is { Length: >= 4 } line)
        {
            var a = ToGui(line[0], line[1], itemCenter);
            var b = ToGui(line[2], line[3], itemCenter);

            if (!IsFinite(a) || !IsFinite(b))
            {
                return false;
            }

            var ab = b - a;
            float length = ab.LengthSquared();
            float t = length > 0f ? Math.Clamp(Vector2.Dot(point - a, ab) / length, 0f, 1f) : 0f;
            nearest = a + ab * t;
            return true;
        }

        if (zone.Rect is { Length: >= 4 } rect)
        {
            var center = ToGui(rect[0], rect[1], itemCenter);
            float halfWidth = Math.Abs(rect[2]) * SlotSize / 2f;
            float halfHeight = Math.Abs(rect[3]) * SlotSize / 2f;

            if (!IsFinite(center) || !float.IsFinite(halfWidth) || !float.IsFinite(halfHeight))
            {
                return false;
            }

            nearest = new Vector2(
                Math.Clamp(point.X, center.X - halfWidth, center.X + halfWidth),
                Math.Clamp(point.Y, center.Y - halfHeight, center.Y + halfHeight));
            return true;
        }

        return false;
    }

    /// <summary> From effect units around the item's centre, +Y up, to GUI units around the slot centre, y down. </summary>
    private static Vector2 ToGui(float x, float y, Vector2 itemCenter) => new(itemCenter.X + x * SlotSize, itemCenter.Y - y * SlotSize);

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
