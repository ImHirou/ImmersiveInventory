using System;
using System.Numerics;

namespace ImmersiveInventory.Physics;

/// <summary>
/// Turns the pivot's positions, one per frame, into an acceleration for <see cref="PendulumBody"/>. Mouse positions are whole
/// pixels and frames uneven, so the velocity is smoothed before it is differentiated; jumps of the cursor (it was warped or the
/// window lost focus) and impossible spikes give no kick.
/// </summary>
public sealed class PivotTracker
{
    /// <summary> Time constant of the velocity smoothing. </summary>
    public float SmoothingSeconds { get; set; } = 0.015f;

    /// <summary> Largest acceleration passed on, GUI units per second squared. </summary>
    public float MaxAcceleration { get; set; } = 40000f;

    /// <summary> A move farther than this within one frame, in GUI units, is a jump of the cursor, not a throw. </summary>
    public float TeleportDistance { get; set; } = 400f;

    private Vector2 lastPosition;
    private bool hasPosition;

    public Vector2 Velocity { get; private set; }

    public Vector2 Acceleration { get; private set; }

    /// <summary> Starts over at <paramref name="position"/>, at rest. </summary>
    public void Reset(Vector2 position)
    {
        lastPosition = position;
        hasPosition = true;
        Velocity = Vector2.Zero;
        Acceleration = Vector2.Zero;
    }

    public void Update(Vector2 position, float seconds)
    {
        if (!hasPosition || !(seconds > 0f) || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            Reset(position);
            return;
        }

        var delta = position - lastPosition;
        lastPosition = position;

        if (delta.Length() > TeleportDistance)
        {
            Velocity = Vector2.Zero;
            Acceleration = Vector2.Zero;
            return;
        }

        float blend = 1f - MathF.Exp(-seconds / Math.Max(1e-4f, SmoothingSeconds));
        var velocity = Velocity + (delta / seconds - Velocity) * blend;
        var acceleration = (velocity - Velocity) / seconds;
        Velocity = velocity;
        float length = acceleration.Length();
        Acceleration = length > MaxAcceleration ? acceleration * (MaxAcceleration / length) : acceleration;
    }
}
