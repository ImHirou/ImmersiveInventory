using System.Numerics;
using ImmersiveInventory.Masks;
using ImmersiveInventory.Physics;
using ImmersiveInventory.Rules;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Gui;

/// <summary>
/// The item on the cursor and how it hangs. Points are GUI units: <see cref="Pivot"/> on the screen, the others around the
/// centre of the slot the item is drawn in, in the item's own frame (before turning by the body's angle).
/// </summary>
internal sealed class HeldItem
{
    public HeldItem(CollectibleObject collectible, ItemStack stack, CompiledRule? rule, Vector2 grab, bool isItem)
    {
        Collectible = collectible;
        Stack = stack;
        Rule = rule;
        RawGrab = grab;
        Grab = grab;
        IsItem = isItem;
    }

    public CollectibleObject Collectible { get; }

    public ItemStack Stack { get; set; }

    /// <summary> The rule that decided, null if none matched. </summary>
    public CompiledRule? Rule { get; }

    /// <summary> Null keeps the stack as in vanilla. </summary>
    public PhysicsProfile? Profile => Rule?.Profile;

    public bool HasPhysics => Profile != null;

    public PendulumBody Body { get; } = new();

    public SwingDetector Swing { get; } = new();

    /// <summary> What is left of the way from the grab point next to the item to the cursor. </summary>
    public SnapSpring Snap;

    /// <summary> Where the click hit, possibly next to the item. </summary>
    public Vector2 RawGrab { get; }

    /// <summary> The point the item hangs from: on the item once the silhouette is known. </summary>
    public Vector2 Grab { get; set; }

    /// <summary> Null until the silhouette is drawn; until then the item doesn't move. </summary>
    public ItemMask? Mask { get; set; }

    /// <summary> From the grab point to the farthest point of the item, the tip. </summary>
    public Vector2 Tip { get; set; }

    /// <summary> Items, unlike blocks, are drawn 3 GUI units left of and 1 above the slot centre. </summary>
    public bool IsItem { get; }

    /// <summary> Where the grab point is on screen. </summary>
    public Vector2 Pivot { get; set; }

    /// <summary> The game shows the stack on the cursor (an inventory is open and the mouse is free). </summary>
    public bool Visible { get; set; }

    /// <summary> Drawing it failed once; it is drawn as in vanilla from then on. </summary>
    public bool RenderFailed { get; set; }
}
