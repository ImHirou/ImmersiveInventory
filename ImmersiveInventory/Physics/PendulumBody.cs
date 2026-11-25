using System;
using System.Numerics;

namespace ImmersiveInventory.Physics;

/// <summary>
/// A flat rigid body hanging from a pivot that moves with the mouse: a physical pendulum. Lengths are GUI units, y points down
/// (like the screen) and a positive angle turns clockwise on screen, the same way <c>GlRotate(angle, 0, 0, 1)</c> turns the GUI.
/// Per unit of mass: the angular acceleration is the torque of gravity and of the pivot's acceleration (an inertial force) about
/// the pivot, divided by the moment of inertia about the pivot (parallel axis theorem), minus damping.
/// </summary>
public sealed class PendulumBody
{
    /// <summary> Fixed simulation step: stable and independent of the frame rate. </summary>
    public const float StepSeconds = 1f / 240f;

    /// <summary> Steps simulated for one frame at most (0.25 s); longer stalls drop the rest instead of catching up. </summary>
    private const int MaxStepsPerAdvance = 60;

    private const float Epsilon = 1e-4f;

    private float accumulator;
    private float cos = 1f;
    private float sin;

    /// <summary> Turn of the item from the way the slot shows it, in radians, between -π and π. </summary>
    public float Angle { get; private set; }

    /// <summary> Radians per second, positive clockwise. </summary>
    public float AngularVelocity { get; set; }

    /// <summary> Centre of mass minus the pivot, in the item's own (unturned) frame. </summary>
    public Vector2 CenterOffset { get; set; }

    /// <summary> Moment of inertia about the centre of mass per unit of mass (squared radius of gyration). </summary>
    public float Gyration { get; set; } = 1f;

    /// <summary> Pull of gravity, GUI units per second squared, towards +y. </summary>
    public float Gravity { get; set; } = 2000f;

    /// <summary> Share of the angular velocity lost per second. </summary>
    public float Damping { get; set; } = 1.8f;

    /// <summary> Quadratic drag on the spin. </summary>
    public float AirDrag { get; set; } = 0.015f;

    /// <summary> Spring pulling the angle back to 0, per second squared. 0 is a free pendulum. </summary>
    public float Stiffness { get; set; }

    /// <summary> Fastest spin, radians per second. </summary>
    public float MaxSpin { get; set; } = 35f;

    /// <summary> Moment of inertia about the pivot per unit of mass. </summary>
    public float Inertia => Gyration + CenterOffset.LengthSquared() + Epsilon;

    public void SetAngle(float angle)
    {
        Angle = Wrap(float.IsFinite(angle) ? angle : 0f);
        cos = MathF.Cos(Angle);
        sin = MathF.Sin(Angle);
    }

    /// <summary> Simulates <paramref name="seconds"/> while the pivot accelerates by <paramref name="pivotAcceleration"/> (GUI units/s²). </summary>
    public void Advance(float seconds, Vector2 pivotAcceleration)
    {
        if (!(seconds > 0f) || !float.IsFinite(seconds))
        {
            return;
        }

        if (!float.IsFinite(pivotAcceleration.X) || !float.IsFinite(pivotAcceleration.Y))
        {
            pivotAcceleration = Vector2.Zero;
        }

        accumulator += seconds;
        int steps = 0;

        while (accumulator >= StepSeconds && steps < MaxStepsPerAdvance)
        {
            Step(StepSeconds, pivotAcceleration);
            accumulator -= StepSeconds;
            steps++;
        }

        if (steps == MaxStepsPerAdvance)
        {
            accumulator = 0f;
        }
    }

    /// <summary> One semi-implicit Euler step. </summary>
    public void Step(float h, Vector2 pivotAcceleration)
    {
        var r = Turn(CenterOffset);
        // In the pivot's frame the body feels gravity and the opposite of the pivot's acceleration.
        float forceX = -pivotAcceleration.X;
        float forceY = Gravity - pivotAcceleration.Y;
        float torque = r.X * forceY - r.Y * forceX;
        float omega = AngularVelocity;
        float alpha = torque / Inertia - Damping * omega - AirDrag * omega * MathF.Abs(omega) - Stiffness * Angle;
        omega = Math.Clamp(omega + alpha * h, -MaxSpin, MaxSpin);

        if (!float.IsFinite(omega))
        {
            omega = 0f;
        }

        AngularVelocity = omega;
        SetAngle(Angle + omega * h);
    }

    /// <summary> Turns a vector of the item's own frame by the current angle. </summary>
    public Vector2 Turn(Vector2 local) => new(cos * local.X - sin * local.Y, sin * local.X + cos * local.Y);

    /// <summary> Kinetic plus potential energy per unit of mass, for a still pivot. </summary>
    public float Energy => 0.5f * Inertia * AngularVelocity * AngularVelocity - Gravity * Turn(CenterOffset).Y;

    private static float Wrap(float angle)
    {
        if (angle > MathF.PI || angle < -MathF.PI)
        {
            angle = MathF.IEEERemainder(angle, MathF.Tau);
        }

        return angle;
    }
}
