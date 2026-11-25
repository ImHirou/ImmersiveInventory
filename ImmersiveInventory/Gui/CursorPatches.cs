using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Gui;

/// <summary>
/// Two patches of the public GUI elements: the passive slot HudMouseTools draws the stack on the cursor with, and the click on a
/// slot of a slot grid. Everything else stays with the game.
/// </summary>
internal static class CursorPatches
{
    private static CursorController? controller;
    private static AccessTools.FieldRef<GuiElementPassiveItemSlot, ItemSlot>? slotOf;

    /// <summary> Fails right away if the game changed, instead of every frame later. </summary>
    public static void Install(Harmony harmony, CursorController instance)
    {
        slotOf = AccessTools.FieldRefAccess<GuiElementPassiveItemSlot, ItemSlot>("slot")
                 ?? throw new MissingFieldException(typeof(GuiElementPassiveItemSlot).FullName, "slot");
        var render = AccessTools.Method(typeof(GuiElementPassiveItemSlot), nameof(GuiElementPassiveItemSlot.RenderInteractiveElements), new[] { typeof(float) })
                     ?? throw new MissingMethodException(typeof(GuiElementPassiveItemSlot).FullName, nameof(GuiElementPassiveItemSlot.RenderInteractiveElements));
        var click = AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick),
                        new[] { typeof(ICoreClientAPI), typeof(int), typeof(EnumMouseButton), typeof(bool), typeof(bool), typeof(bool) })
                    ?? throw new MissingMethodException(typeof(GuiElementItemSlotGridBase).FullName, nameof(GuiElementItemSlotGridBase.SlotClick));

        controller = instance;
        harmony.Patch(render, prefix: new HarmonyMethod(typeof(CursorPatches), nameof(BeforeRenderPassiveSlot)));
        harmony.Patch(click, prefix: new HarmonyMethod(typeof(CursorPatches), nameof(BeforeSlotClick)),
            postfix: new HarmonyMethod(typeof(CursorPatches), nameof(AfterSlotClick)));
    }

    public static void Uninstall(CursorController instance)
    {
        if (ReferenceEquals(controller, instance))
        {
            controller = null;
        }
    }

    /// <summary> False skips the game's drawing (with its scissor, which would cut off a turned item). </summary>
    private static bool BeforeRenderPassiveSlot(GuiElementPassiveItemSlot __instance)
    {
        var instance = controller;
        var slot = slotOf;
        return instance == null || slot == null || !instance.RenderCursorStack(slot(__instance));
    }

    private static void BeforeSlotClick(GuiElementItemSlotGridBase __instance, int slotId) => controller?.BeforeSlotClick(__instance, slotId);

    private static void AfterSlotClick() => controller?.AfterSlotClick();
}
