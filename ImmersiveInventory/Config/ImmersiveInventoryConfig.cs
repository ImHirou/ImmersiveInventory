using System;
using System.Collections.Generic;
using System.Linq;

namespace ImmersiveInventory.Config;

/// <summary> Client settings, stored in <c>ModConfig/immersiveinventory.json</c>. Lengths are GUI pixels at GUI scale 1. </summary>
public sealed class ImmersiveInventoryConfig
{
    public const string FileName = "immersiveinventory.json";
    public const int CurrentVersion = 1;

    public int ConfigVersion = CurrentVersion;

    /// <summary> Turns the physics on or off. Off, the stack on the cursor is drawn as in vanilla. </summary>
    public bool Enabled = true;

    /// <summary> How hard the centre of mass is pulled down, in GUI pixels per second squared. More swings faster. </summary>
    public float Gravity = 2000f;

    /// <summary> Share of the spin lost per second. More calms the swinging down sooner. </summary>
    public float Damping = 1.8f;

    /// <summary> Extra braking of fast spins, like air drag. </summary>
    public float AirDrag = 0.015f;

    /// <summary> Fastest spin in radians per second (35 is about five and a half turns a second). </summary>
    public float MaxSpin = 35f;

    /// <summary> How much moving the mouse swings the item: 0 only gravity, 1 physical, more exaggerates. </summary>
    public float MouseInfluence = 1f;

    /// <summary> Seconds a grab point next to the item takes to come to the cursor. </summary>
    public float SnapTime = 0.06f;

    /// <summary> Show swing particles. Needs Inventory Particles. </summary>
    public bool SwingParticles = true;

    /// <summary> Speed of the tip around the grab point from which a swing shows particles, in GUI pixels per second, unless a rule sets its own. </summary>
    public float SwingSpeed = 420f;

    /// <summary> Codes of rules to ignore, e.g. <c>weapons</c>. <c>.immersiveinventory list</c> shows them. </summary>
    public List<string> DisabledRules = new();

    internal void Sanitize()
    {
        var defaults = new ImmersiveInventoryConfig();
        Gravity = Sanitize(Gravity, defaults.Gravity, 0f, 100000f);
        Damping = Sanitize(Damping, defaults.Damping, 0f, 100f);
        AirDrag = Sanitize(AirDrag, defaults.AirDrag, 0f, 10f);
        MaxSpin = Sanitize(MaxSpin, defaults.MaxSpin, 1f, 200f);
        MouseInfluence = Sanitize(MouseInfluence, defaults.MouseInfluence, 0f, 10f);
        SnapTime = Sanitize(SnapTime, defaults.SnapTime, 0f, 2f);
        SwingSpeed = Sanitize(SwingSpeed, defaults.SwingSpeed, 1f, 100000f);
        DisabledRules = (DisabledRules ?? new List<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static float Sanitize(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
