using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ImmersiveInventory.Masks;

/// <summary>
/// Draws the silhouettes of items into a small offscreen framebuffer and reads them back, one per frame. It works like the game's
/// own drawing of stacks into texture atlases (<c>RenderItemStackToAtlas</c>): at the end of the ortho stage, right after it, with
/// the GUI shader in use and the same calls, so every item looks exactly as it does in a slot, custom renderers included.
/// </summary>
internal sealed class ItemMaskRenderer : IRenderer
{
    /// <summary> The game draws stacks into atlases at 9 of the ortho stage. </summary>
    public const double Order = 9.1;

    /// <summary> Radius of the stand-in for items whose silhouette can't be drawn, in GUI units. </summary>
    public const float FallbackRadius = 0.4f * 48f;

    /// <summary> The drawn square spans this many item sizes; items hardly ever reach past their slot. </summary>
    private const float CoverageItemSizes = 3f;

    private const double Depth = 250;

    private static readonly float[] Transparent = { 0f, 0f, 0f, 0f };

    private readonly ICoreClientAPI capi;
    private readonly ItemMaskCache cache;
    private readonly ILogger logger;
    private readonly int[] viewport = new int[4];
    private FrameBufferRef? framebuffer;
    private int framebufferSize;
    private byte[] pixels = Array.Empty<byte>();
    private bool broken;

    public ItemMaskRenderer(ICoreClientAPI capi, ItemMaskCache cache, ILogger logger)
    {
        this.capi = capi;
        this.cache = cache;
        this.logger = logger;
    }

    public double RenderOrder => Order;

    public int RenderRange => 0;

    /// <summary> Edge of the drawn square in pixels for an item of <paramref name="itemSize"/> pixels. </summary>
    public static int SizeFor(float itemSize) => Math.Clamp((int)MathF.Ceiling(itemSize * CoverageItemSizes / 16f) * 16, 32, 1024);

    public static ItemMask Fallback(float guiScale) => ItemMask.Disc(FallbackRadius, guiScale);

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!cache.TryDequeue(out var request))
        {
            return;
        }

        float scale = RuntimeEnv.GUIScale;

        // Asked for at another GUI scale: whoever still needs it asks again.
        if (request.Key.Scale != MaskKey.ScaleKey(scale))
        {
            return;
        }

        if (broken)
        {
            cache.Store(request.Key, Fallback(scale));
            return;
        }

        try
        {
            cache.Store(request.Key, Draw(request.Stack, scale));
        }
        catch (Exception e)
        {
            broken = true;
            logger.Error("Could not draw the silhouette of {0}; items swing around round stand-ins from now on: {1}", request.Stack.Collectible?.Code, e);
            cache.Store(request.Key, Fallback(scale));
        }
    }

    private ItemMask Draw(ItemStack stack, float scale)
    {
        var render = capi.Render;
        float itemSize = (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize);
        int size = SizeFor(itemSize);
        var previous = render.CurrentFrameBuffer;
        GL.GetInteger(GetPName.Viewport, viewport);
        bool depthTest = GL.IsEnabled(EnableCap.DepthTest);
        bool scissorTest = GL.IsEnabled(EnableCap.ScissorTest);
        bool cullFace = GL.IsEnabled(EnableCap.CullFace);
        bool ortho = false;

        try
        {
            EnsureFramebuffer(size);
            render.CurrentFrameBuffer = framebuffer;
            render.ClearFrameBuffer(framebuffer, Transparent, true, true);
            // Rows come back bottom up; flipping the projection makes the first row the top of the slot.
            render.OrthoMode(size, size, true);
            ortho = true;

            if (scissorTest)
            {
                render.GlScissorFlag(false);
            }

            if (!depthTest)
            {
                render.GLEnableDepthTest();
            }

            if (cullFace)
            {
                render.GlDisableCullFace();
            }

            render.RenderItemstackToGui(new DummySlot(stack), size / 2.0, size / 2.0, Depth, itemSize, ColorUtil.WhiteArgb, false, false, false);
            GL.ReadPixels(0, 0, size, size, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        }
        finally
        {
            if (ortho)
            {
                render.PMatrix.Pop();
                render.MvMatrix.Pop();
            }

            render.CurrentFrameBuffer = previous;
            render.GlViewport(viewport[0], viewport[1], viewport[2], viewport[3]);

            if (scissorTest)
            {
                render.GlScissorFlag(true);
            }

            if (!depthTest)
            {
                render.GLDisableDepthTest();
            }

            if (cullFace)
            {
                render.GlEnableCullFace();
            }
        }

        return ItemMask.FromRgba(pixels, size, scale);
    }

    private void EnsureFramebuffer(int size)
    {
        if (framebuffer != null && framebufferSize == size)
        {
            return;
        }

        DisposeFramebuffer();
        framebuffer = capi.Render.CreateFrameBuffer(new FramebufferAttrs("immersiveinventory-mask", size, size)
        {
            Attachments = new[]
            {
                new FramebufferAttrsAttachment
                {
                    AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                    Texture = new RawTexture
                    {
                        Width = size,
                        Height = size,
                        PixelFormat = EnumTexturePixelFormat.Rgba,
                        PixelInternalFormat = EnumTextureInternalFormat.Rgba8
                    }
                },
                new FramebufferAttrsAttachment
                {
                    AttachmentType = EnumFramebufferAttachment.DepthAttachment,
                    Texture = new RawTexture
                    {
                        Width = size,
                        Height = size,
                        PixelFormat = EnumTexturePixelFormat.DepthComponent,
                        PixelInternalFormat = EnumTextureInternalFormat.DepthComponent32
                    }
                }
            }
        });
        framebufferSize = size;
        pixels = new byte[size * size * 4];
    }

    private void DisposeFramebuffer()
    {
        if (framebuffer != null)
        {
            capi.Render.DestroyFrameBuffer(framebuffer);
            framebuffer = null;
        }

        framebufferSize = 0;
    }

    public void Dispose() => DisposeFramebuffer();
}
