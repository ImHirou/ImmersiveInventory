using Newtonsoft.Json;

namespace ImmersiveInventory.Rules;

/// <summary>
/// Decides whether stacks swing on the cursor and how. Rules come from
/// <c>assets/&lt;domain&gt;/config/immersiveinventory/rules/*.json</c> of every mod or from code; they are checked from the
/// highest <see cref="Priority"/> to the lowest and the first one that matches decides.
/// </summary>
public sealed class PhysicsRule
{
    /// <summary> Unique name. The config's <c>DisabledRules</c> refers to it. </summary>
    public string? Code;

    public float Priority;

    public RuleMatch? Match;

    /// <summary> False keeps the matched stacks as in vanilla. </summary>
    public bool Physics = true;

    /// <summary> Multiplies the config's gravity: more makes the item hang and swing back faster. </summary>
    public float Gravity = 1f;

    /// <summary> Multiplies the config's damping. </summary>
    public float Damping = 1f;

    /// <summary> Multiplies the item's moment of inertia around its centre of mass: more turns slower. </summary>
    public float Inertia = 1f;

    /// <summary> How firmly the item keeps the angle it had in the slot, per second squared. 0 is a free pendulum. </summary>
    public float Stiffness;

    /// <summary> Inventory Particles effect played along the item while it swings fast, e.g. <c>swing</c>. Without a domain, the rule file's. </summary>
    public string? Swing;

    /// <summary> Speed of the tip around the grab point for <see cref="Swing"/>, GUI pixels per second; null uses the config's. </summary>
    public float? SwingSpeed;

    /// <summary> Places the item can be held by. Without them the grab point snaps to the item's silhouette. </summary>
    public GripZone[]? Grip;

    /// <summary> Domain of the file the rule came from. </summary>
    [JsonIgnore]
    public string? Domain;
}

/// <summary> What a rule applies to. All given conditions must hold; within a list, one entry is enough. </summary>
public sealed class RuleMatch
{
    /// <summary> Item and block codes with <c>*</c> wildcards; a leading <c>@</c> makes a regular expression. Without a domain, <c>game</c>. </summary>
    public string[]? Codes;

    /// <summary> Registered class names (<c>BlockTorch</c>, <c>ItemLiquidPortion</c>…); subclasses match too. </summary>
    public string[]? Classes;

    /// <summary> Tool types (<c>sword</c>, <c>axe</c>…), see EnumTool. </summary>
    public string[]? Tools;

    /// <summary> <c>item</c> or <c>block</c>. </summary>
    public string? Type;

    /// <summary> True for edible collectibles (with nutrition), false for the others. </summary>
    public bool? Food;

    /// <summary> Draw types of blocks (<c>Cross</c>, <c>JSON</c>…); a trailing <c>*</c> matches the beginning. </summary>
    public string[]? DrawTypes;
}

/// <summary>
/// A place an item can be held by, in effect units around the centre of the item as drawn in a slot: 1.0 is the edge of a slot,
/// +Y is up (like the origins of Inventory Particles). Give one of <see cref="Point"/>, <see cref="Line"/> or <see cref="Rect"/>.
/// </summary>
public sealed class GripZone
{
    /// <summary> <c>[x, y]</c> </summary>
    public float[]? Point;

    /// <summary> <c>[x1, y1, x2, y2]</c> </summary>
    public float[]? Line;

    /// <summary> <c>[centreX, centreY, width, height]</c> </summary>
    public float[]? Rect;
}
