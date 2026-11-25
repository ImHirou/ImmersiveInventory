using System;
using System.Collections.Generic;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace ImmersiveInventory.Gui;

/// <summary>
/// The stack size next to the cursor, drawn upright where and how InventoryItemRenderer draws it for the cursor slot,
/// because the game's own label would turn with the item.
/// </summary>
internal sealed class StackSizeLabel : IDisposable
{
    private const int MaxTextures = 256;

    private readonly ICoreClientAPI capi;
    private readonly Dictionary<int, LoadedTexture> textures = new();
    private float scale;

    public StackSizeLabel(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary> Draws <paramref name="stackSize"/> for a stack on the cursor at the mouse position (screen pixels). </summary>
    public void Render(int stackSize, double mouseX, double mouseY, double depth)
    {
        var texture = Get(stackSize);
        // The game: centre of the cursor slot (mouse + 17) + item size (25.6) + 1 - width, centre + 3 - 4.
        float offset = CursorController.VanillaCursorOffset;
        float x = (int)(mouseX + GuiElement.scaled(offset + GuiElementPassiveItemSlot.unscaledItemSize + 1) - texture.Width);
        float y = (int)(mouseY + GuiElement.scaled(offset + 3 - 4));
        capi.Render.Render2DLoadedTexture(texture, x, y, (float)depth + 100f);
    }

    private LoadedTexture Get(int stackSize)
    {
        if (scale != RuntimeEnv.GUIScale || textures.Count >= MaxTextures)
        {
            Clear();
            scale = RuntimeEnv.GUIScale;
        }

        if (!textures.TryGetValue(stackSize, out var texture))
        {
            var font = CairoFont.WhiteSmallText().WithFontSize((float)GuiStyle.SmallishFontSize);
            font.FontWeight = FontWeight.Bold;
            font.StrokeColor = new double[] { 0, 0, 0, 1 };
            font.StrokeWidth = RuntimeEnv.GUIScale / RuntimeEnv.DPIHScale + 0.25;
            texture = capi.Gui.TextTexture.GenTextTexture(stackSize.ToString(), font, null);
            textures[stackSize] = texture;
        }

        return texture;
    }

    public void Clear()
    {
        foreach (var texture in textures.Values)
        {
            texture.Dispose();
        }

        textures.Clear();
    }

    public void Dispose() => Clear();
}
