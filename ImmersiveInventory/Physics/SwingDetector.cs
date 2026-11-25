using System;

namespace ImmersiveInventory.Physics;

/// <summary>
/// Tells a swing from wobbling: active once the tip moves faster than <see cref="Threshold"/> around the grab point, and over once
/// it stays below <see cref="ReleaseFactor"/> of that for <see cref="HoldSeconds"/>.
/// </summary>
public sealed class SwingDetector
{
    public float Threshold { get; set; } = 420f;

    public float ReleaseFactor { get; set; } = 0.6f;

    public float HoldSeconds { get; set; } = 0.05f;

    private float slowFor;

    public bool Active { get; private set; }

    /// <summary> 0 while no swing is going on, otherwise the speed relative to the threshold (0.3 to 2.5). </summary>
    public float Intensity { get; private set; }

    public void Update(float tipSpeed, float seconds)
    {
        if (!float.IsFinite(tipSpeed))
        {
            tipSpeed = 0f;
        }

        if (!Active)
        {
            if (tipSpeed > Threshold)
            {
                Active = true;
                slowFor = 0f;
            }
        }
        else if (tipSpeed < Threshold * ReleaseFactor)
        {
            slowFor += Math.Max(0f, seconds);

            if (slowFor >= HoldSeconds)
            {
                Active = false;
            }
        }
        else
        {
            slowFor = 0f;
        }

        Intensity = Active ? Math.Clamp(tipSpeed / Math.Max(1f, Threshold), 0.3f, 2.5f) : 0f;
    }

    public void Reset()
    {
        Active = false;
        Intensity = 0f;
        slowFor = 0f;
    }
}
