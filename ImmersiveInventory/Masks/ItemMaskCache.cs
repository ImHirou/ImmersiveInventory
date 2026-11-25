using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Masks;

/// <summary> One silhouette per item type and GUI scale (in hundredths). </summary>
internal readonly record struct MaskKey(EnumItemClass Class, int Id, int Scale)
{
    public static MaskKey Of(ItemStack stack, float guiScale) => new(stack.Class, stack.Id, ScaleKey(guiScale));

    public static int ScaleKey(float guiScale) => (int)MathF.Round(guiScale * 100f);
}

internal readonly record struct MaskRequest(MaskKey Key, ItemStack Stack);

/// <summary> Silhouettes drawn so far and the stacks still waiting for theirs. </summary>
internal sealed class ItemMaskCache
{
    /// <summary> More item types than this in one session start the cache over. </summary>
    public const int MaxMasks = 1024;

    private readonly Dictionary<MaskKey, ItemMask> masks = new();
    private readonly Queue<MaskRequest> queue = new();
    private readonly HashSet<MaskKey> pending = new();

    public int Count => masks.Count;

    public int PendingCount => queue.Count;

    public bool TryGet(ItemStack stack, float guiScale, out ItemMask mask) => masks.TryGetValue(MaskKey.Of(stack, guiScale), out mask!);

    public bool Contains(ItemStack stack, float guiScale) => masks.ContainsKey(MaskKey.Of(stack, guiScale));

    /// <summary> Asks for the silhouette of <paramref name="stack"/>; a copy of the stack waits until the renderer draws it. </summary>
    public void Request(ItemStack stack, float guiScale)
    {
        var key = MaskKey.Of(stack, guiScale);

        if (masks.ContainsKey(key) || !pending.Add(key))
        {
            return;
        }

        queue.Enqueue(new MaskRequest(key, stack.Clone()));
    }

    public bool TryDequeue(out MaskRequest request)
    {
        if (queue.Count == 0)
        {
            request = default;
            return false;
        }

        request = queue.Dequeue();
        pending.Remove(request.Key);
        return true;
    }

    public void Store(MaskKey key, ItemMask mask)
    {
        if (masks.Count >= MaxMasks)
        {
            masks.Clear();
        }

        masks[key] = mask;
    }

    public void Clear()
    {
        masks.Clear();
        queue.Clear();
        pending.Clear();
    }
}
