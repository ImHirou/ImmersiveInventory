using System.Numerics;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Gui;

/// <summary> A click that just put a stack on the cursor, and where on its slot it hit. </summary>
/// <param name="Offset"> The mouse minus the centre of the clicked slot, in GUI units. </param>
/// <param name="Time"> <see cref="CursorController.Time"/> of the click. </param>
internal readonly record struct PendingGrab(CollectibleObject Collectible, Vector2 Offset, double Time);

/// <summary>
/// Remembers where a slot was clicked when that click puts a new stack on the cursor: picking a stack up, taking half of it,
/// swapping it with the one on the cursor or taking one from the creative inventory. Merging and putting items down don't count.
/// </summary>
internal sealed class GrabCapture
{
    /// <summary> A click older than this no longer explains the stack on the cursor. </summary>
    public const double MaxAgeSeconds = 0.5;

    private ItemStack? mouseStackBefore;
    private Vector2 offset;
    private bool armed;
    private PendingGrab? pending;

    public PendingGrab? Pending => pending;

    /// <summary> A slot is about to be clicked. Positions are in GUI units. </summary>
    public void Before(ItemStack? mouseStack, Vector2 mouse, Vector2 slotCenter)
    {
        mouseStackBefore = mouseStack;
        offset = mouse - slotCenter;
        armed = true;
    }

    /// <summary> The click was handled; <paramref name="mouseStack"/> is on the cursor now. </summary>
    public void After(ItemStack? mouseStack, double time)
    {
        if (!armed)
        {
            return;
        }

        armed = false;
        var before = mouseStackBefore;
        mouseStackBefore = null;

        // The same stack on the cursor (fewer items after putting some down) is no new grab.
        if (mouseStack?.Collectible != null && !ReferenceEquals(mouseStack, before))
        {
            pending = new PendingGrab(mouseStack.Collectible, offset, time);
        }
    }

    /// <summary> Takes the pending grab if it is recent and for <paramref name="collectible"/>. </summary>
    public bool TryTake(CollectibleObject collectible, double now, out PendingGrab grab)
    {
        grab = default;

        if (pending is not { } candidate)
        {
            return false;
        }

        if (now - candidate.Time > MaxAgeSeconds)
        {
            pending = null;
            return false;
        }

        if (!ReferenceEquals(candidate.Collectible, collectible))
        {
            return false;
        }

        pending = null;
        grab = candidate;
        return true;
    }

    /// <summary> The coming click can't be measured; a grab still pending stays. </summary>
    public void Disarm()
    {
        armed = false;
        mouseStackBefore = null;
    }

    public void Clear()
    {
        pending = null;
        Disarm();
    }
}
