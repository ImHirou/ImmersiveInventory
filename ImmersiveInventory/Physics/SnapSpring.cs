using System;
using System.Numerics;

namespace ImmersiveInventory.Physics;

/// <summary>
/// An offset that dies out like a critically damped spring: o(t) = o₀ (1 + t/τ) e^(−t/τ). It brings a grab point next to the
/// item to the cursor without the item jumping; about 90 % of the way is done after <c>duration</c>.
/// </summary>
public struct SnapSpring
{
    private Vector2 start;
    private double startTime;
    private float tau;

    public void Start(Vector2 offset, double time, float duration)
    {
        start = float.IsFinite(offset.X) && float.IsFinite(offset.Y) ? offset : Vector2.Zero;
        startTime = time;
        tau = Math.Max(duration, 1e-4f) / 4f;
    }

    public readonly Vector2 Offset(double time)
    {
        if (start == Vector2.Zero)
        {
            return Vector2.Zero;
        }

        float elapsed = (float)(time - startTime);

        if (elapsed <= 0f)
        {
            return start;
        }

        float x = elapsed / tau;
        return x > 16f ? Vector2.Zero : start * ((1f + x) * MathF.Exp(-x));
    }
}
