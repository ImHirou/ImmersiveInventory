using System;
using System.Numerics;
using ImmersiveInventory.Config;
using ImmersiveInventory.Masks;
using ImmersiveInventory.Physics;
using ImmersiveInventory.Rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace ImmersiveInventory.Gui;

/// <summary>
/// Runs the item on the mouse cursor: notices when a stack arrives there, lets it hang from where it was grabbed, simulates it
/// every frame before the GUI is drawn and draws it in place of the game's own drawing (called from <see cref="CursorPatches"/>).
/// </summary>
internal sealed class CursorController : IDisposable
{
    /// <summary> HudMouseTools draws the stack on the cursor in a slot whose centre is this far right of and below the mouse. </summary>
    public const float VanillaCursorOffset = 5f + 48f / 4f;

    /// <summary> Depth the game draws the stack on the cursor at (HudMouseTools adds 160 on top). </summary>
    public const double CursorDepth = 450;

    private const float MaxFrameSeconds = 0.1f;

    /// <summary> InventoryItemRenderer draws items (not blocks) this far from the slot centre. </summary>
    private static readonly Vector2 ItemShift = new(-3f, -1f);

    /// <summary> Without a click to go by, the item stays where the game would draw it and hangs from the point under the mouse. </summary>
    private static readonly Vector2 VanillaGrab = new(-VanillaCursorOffset, -VanillaCursorOffset);

    private static readonly int PivotColor = ColorUtil.ToRgba(255, 255, 60, 60);
    private static readonly int CenterColor = ColorUtil.ToRgba(255, 70, 140, 255);
    private static readonly int TipColor = ColorUtil.ToRgba(255, 80, 230, 80);

    private readonly ICoreClientAPI capi;
    private readonly RuleRegistry rules;
    private readonly ItemMaskCache masks;
    private readonly ILogger logger;
    private readonly PivotTracker tracker = new();
    private readonly StackSizeLabel label;
    private ImmersiveInventoryConfig config;
    private HeldItem? held;
    private bool updateErrorLogged;
    private bool renderErrorLogged;
    private bool clickErrorLogged;

    public CursorController(ICoreClientAPI capi, RuleRegistry rules, ItemMaskCache masks, ImmersiveInventoryConfig config, ILogger logger)
    {
        this.capi = capi;
        this.rules = rules;
        this.masks = masks;
        this.config = config;
        this.logger = logger;
        label = new StackSizeLabel(capi);
    }

    public GrabCapture Grabs { get; } = new();

    /// <summary> Seconds of frames seen so far. </summary>
    public double Time { get; private set; }

    /// <summary> Draw the grab point, the centre of mass and the tip, and report rules in chat. </summary>
    public bool Debug { get; set; }

    public HeldItem? Held => held;

    public ImmersiveInventoryConfig Config => config;

    private static float Scale => RuntimeEnv.GUIScale > 0f && float.IsFinite(RuntimeEnv.GUIScale) ? RuntimeEnv.GUIScale : 1f;

    private static float ItemSize => (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledItemSize);

    public void ApplyConfig(ImmersiveInventoryConfig settings)
    {
        config = settings;
        Release();
    }

    /// <summary> Forgets the item on the cursor; it is picked up again with the current rules and settings. </summary>
    public void Reset()
    {
        Release();
        Grabs.Clear();
        label.Clear();
    }

    #region Frame

    /// <summary> Called every frame before the GUI is drawn. </summary>
    public void Update(float deltaTime)
    {
        try
        {
            UpdateHeld(deltaTime);
        }
        catch (Exception e)
        {
            if (!updateErrorLogged)
            {
                updateErrorLogged = true;
                logger.Error("The item on the cursor could not be simulated; further errors are not logged: {0}", e);
            }

            Release();
        }
    }

    private void UpdateHeld(float deltaTime)
    {
        float dt = float.IsFinite(deltaTime) ? Math.Clamp(deltaTime, 0f, MaxFrameSeconds) : 0f;
        Time += dt;
        var inventory = capi.World?.Player?.InventoryManager;
        var stack = inventory?.MouseItemSlot?.Itemstack;

        if (!config.Enabled || inventory == null || stack?.Collectible == null)
        {
            Release();
            return;
        }

        float scale = Scale;
        var mouse = new Vector2(capi.Input.MouseX, capi.Input.MouseY) / scale;

        // The server replaces the stack on the cursor with its own copy now and then: only a click or another item starts over.
        if (Grabs.TryTake(stack.Collectible, Time, out var grab))
        {
            Begin(stack, grab.Offset, mouse);
        }
        else if (held == null || !ReferenceEquals(held.Collectible, stack.Collectible))
        {
            Begin(stack, VanillaGrab, mouse);
        }

        var item = held!;
        item.Stack = stack;
        Prewarm(inventory, scale);

        if (!item.HasPhysics)
        {
            return;
        }

        // The same condition under which HudMouseTools draws the stack on the cursor.
        bool visible = !capi.Input.MouseGrabbed && inventory.OpenedInventories.Count > 0;
        bool shown = visible && !item.Visible;
        item.Visible = visible;

        if (!visible)
        {
            // Frozen while the game doesn't show it.
            item.Swing.Reset();
            return;
        }

        bool attached = false;

        if (item.Mask == null)
        {
            if (masks.TryGet(stack, scale, out var mask))
            {
                Attach(item, mask);
                attached = true;
            }
            else
            {
                masks.Request(stack, scale);
            }
        }

        item.Pivot = mouse + item.Snap.Offset(Time);

        if (attached || shown)
        {
            // The pivot moved over to the grab point on the item, or the mouse moved while the stack was hidden: no throw.
            tracker.Reset(item.Pivot);
        }
        else
        {
            tracker.Update(item.Pivot, dt);
        }

        if (item.Mask != null)
        {
            item.Body.Advance(dt, tracker.Acceleration * config.MouseInfluence);
            item.Swing.Update(MathF.Abs(item.Body.AngularVelocity) * item.Tip.Length(), dt);
        }
    }

    /// <summary> A new stack on the cursor: it hangs from <paramref name="grab"/> (around its slot centre, GUI units). </summary>
    private void Begin(ItemStack stack, Vector2 grab, Vector2 mouse)
    {
        var rule = rules.Pick(stack.Collectible);
        var item = new HeldItem(stack.Collectible, stack, rule, grab, stack.Class == EnumItemClass.Item) { Pivot = mouse };

        if (item.Profile is { } profile)
        {
            var body = item.Body;
            body.Gravity = config.Gravity * profile.Gravity;
            body.Damping = config.Damping * profile.Damping;
            body.AirDrag = config.AirDrag;
            body.Stiffness = profile.Stiffness;
            body.MaxSpin = config.MaxSpin;
            item.Swing.Threshold = profile.SwingSpeed ?? config.SwingSpeed;
        }

        held = item;
        tracker.Reset(mouse);

        if (Debug)
        {
            capi.ShowChatMessage(Lang.Get($"{ImmersiveInventoryModSystem.Domain}:debug-held", stack.Collectible.Code, rule?.Code ?? "-",
                Lang.Get(item.HasPhysics ? $"{ImmersiveInventoryModSystem.Domain}:debug-physics-on" : $"{ImmersiveInventoryModSystem.Domain}:debug-physics-off")));
        }
    }

    /// <summary> The silhouette is known: the item hangs from the nearest point on it, its centre of mass and tip are known. </summary>
    private void Attach(HeldItem item, ItemMask mask)
    {
        var profile = item.Profile!;
        var grab = profile.Grip != null
            ? GripGeometry.Nearest(profile.Grip, item.RawGrab, item.IsItem ? ItemShift : Vector2.Zero)
            : mask.Nearest(item.RawGrab);

        item.Mask = mask;
        // The item stays where it is; the grab point glides to the cursor instead. The angle is still 0.
        item.Snap.Start(grab - item.RawGrab, Time, config.SnapTime);
        item.Grab = grab;
        item.Body.CenterOffset = mask.Centroid - grab;
        item.Body.Gyration = mask.GyrationSquared * profile.Inertia;
        item.Tip = mask.Farthest(grab) - grab;

        if (Debug)
        {
            var center = item.Body.CenterOffset;
            capi.ShowChatMessage(Lang.Get($"{ImmersiveInventoryModSystem.Domain}:debug-mask", mask.Area,
                $"{center.X:0.#}, {center.Y:0.#}", $"{MathF.Sqrt(mask.GyrationSquared):0.#}", $"{item.Tip.Length():0.#}"));
        }
    }

    /// <summary> Asks early for the silhouette of the stack under the mouse, so that it is ready when that stack is picked up. </summary>
    private void Prewarm(IPlayerInventoryManager inventory, float scale)
    {
        var hovered = inventory.CurrentHoveredSlot?.Itemstack;

        if (hovered?.Collectible != null && !masks.Contains(hovered, scale) && rules.Resolve(hovered.Collectible) != null)
        {
            masks.Request(hovered, scale);
        }
    }

    private void Release() => held = null;

    #endregion

    #region Drawing

    /// <summary>
    /// Draws the stack of <paramref name="slot"/> if it is the stack on the cursor and swings. False lets the game draw it.
    /// Called from the prefix of <c>GuiElementPassiveItemSlot.RenderInteractiveElements</c>, inside HudMouseTools' matrix.
    /// </summary>
    public bool RenderCursorStack(ItemSlot? slot)
    {
        var item = held;

        if (item == null || !item.HasPhysics || item.RenderFailed || slot == null)
        {
            return false;
        }

        if (!ReferenceEquals(slot, capi.World?.Player?.InventoryManager?.MouseItemSlot) || !ReferenceEquals(slot.Itemstack?.Collectible, item.Collectible))
        {
            return false;
        }

        try
        {
            Draw(slot, item);
            return true;
        }
        catch (Exception e)
        {
            item.RenderFailed = true;

            if (!renderErrorLogged)
            {
                renderErrorLogged = true;
                logger.Error("Could not draw {0} on the cursor, it is drawn as in vanilla; further errors are not logged: {1}", item.Collectible.Code, e);
            }

            return false;
        }
    }

    private void Draw(ItemSlot slot, HeldItem item)
    {
        var render = capi.Render;
        float scale = Scale;
        var pivot = item.Pivot * scale;
        render.GlPushMatrix();

        try
        {
            // The grab point sits on the cursor and the item turns around it.
            render.GlTranslate(pivot.X, pivot.Y, 0f);
            render.GlRotate(item.Body.Angle * GameMath.RAD2DEG, 0f, 0f, 1f);
            render.RenderItemstackToGui(slot, -item.Grab.X * scale, -item.Grab.Y * scale, CursorDepth, ItemSize, ColorUtil.WhiteArgb, true, false, false);
        }
        finally
        {
            render.GlPopMatrix();
        }

        // The stack size stays upright where the game shows it.
        if (slot.Itemstack is { StackSize: > 1 } stack)
        {
            label.Render(stack.StackSize, capi.Input.MouseX, capi.Input.MouseY, CursorDepth);
        }

        if (Debug)
        {
            DrawMarkers(item, scale);
        }
    }

    private void DrawMarkers(HeldItem item, float scale)
    {
        Marker(item.Pivot, PivotColor);

        if (item.Mask != null)
        {
            Marker(item.Pivot + item.Body.Turn(item.Body.CenterOffset), CenterColor);
            Marker(item.Pivot + item.Body.Turn(item.Tip), TipColor);
        }

        void Marker(Vector2 point, int color)
        {
            float half = 2f * scale;
            capi.Render.RenderRectangle(point.X * scale - half, point.Y * scale - half, (float)CursorDepth + 200f, half * 2f, half * 2f, color);
        }
    }

    #endregion

    #region Queries for the particles

    /// <summary> Where the item's centre is drawn (as Inventory Particles measures it), in screen pixels, and its angle. </summary>
    public bool TryGetItemRect(out float x, out float y, out float size, out float rotation)
    {
        var item = held;

        if (item == null || !item.HasPhysics || !item.Visible || item.RenderFailed)
        {
            x = y = size = rotation = 0f;
            return false;
        }

        float scale = Scale;
        var center = (item.Pivot + item.Body.Turn((item.IsItem ? ItemShift : Vector2.Zero) - item.Grab)) * scale;
        x = center.X;
        y = center.Y;
        size = (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);
        rotation = item.Body.Angle;
        return true;
    }

    /// <summary> The swing effect to show now and how strong, if the item swings fast. </summary>
    public bool TryGetSwing(out AssetLocation? effect, out float intensity)
    {
        var item = held;

        if (item?.Profile?.Swing is { } code && item.Visible && !item.RenderFailed && item.Swing.Active)
        {
            effect = code;
            intensity = item.Swing.Intensity;
            return true;
        }

        effect = null;
        intensity = 0f;
        return false;
    }

    /// <summary> A point on the line from the grab point (0) to the tip (1), in screen pixels. </summary>
    public bool TryGetSwingPoint(float fraction, out float x, out float y)
    {
        var item = held;

        if (item?.Mask == null || !item.Visible)
        {
            x = y = 0f;
            return false;
        }

        var point = (item.Pivot + item.Body.Turn(item.Tip * fraction)) * Scale;
        x = point.X;
        y = point.Y;
        return true;
    }

    #endregion

    #region Clicks

    /// <summary> Prefix of <c>GuiElementItemSlotGridBase.SlotClick</c>: where on the slot the click is. </summary>
    public void BeforeSlotClick(GuiElementItemSlotGridBase grid, int slotId)
    {
        try
        {
            var inventory = capi.World?.Player?.InventoryManager;
            var bounds = grid.SlotBounds;
            int index = grid.renderedSlots?.IndexOfKey(slotId) ?? -1;

            if (inventory == null || bounds == null || index < 0 || index >= bounds.Length || bounds[index] == null)
            {
                Grabs.Disarm();
                return;
            }

            float scale = Scale;
            double half = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize) / 2.0;
            var slotCenter = new Vector2((float)(bounds[index].renderX + half), (float)(bounds[index].renderY + half)) / scale;
            var mouse = new Vector2(capi.Input.MouseX, capi.Input.MouseY) / scale;
            Grabs.Before(inventory.MouseItemSlot?.Itemstack, mouse, slotCenter);
        }
        catch (Exception e)
        {
            Grabs.Disarm();
            ReportClickError(e);
        }
    }

    /// <summary> Postfix of <c>GuiElementItemSlotGridBase.SlotClick</c>: did the click put a new stack on the cursor? </summary>
    public void AfterSlotClick()
    {
        try
        {
            Grabs.After(capi.World?.Player?.InventoryManager?.MouseItemSlot?.Itemstack, Time);
        }
        catch (Exception e)
        {
            ReportClickError(e);
        }
    }

    private void ReportClickError(Exception e)
    {
        if (!clickErrorLogged)
        {
            clickErrorLogged = true;
            logger.Error("Could not tell where a slot was clicked; items are grabbed by their default point; further errors are not logged: {0}", e);
        }
    }

    #endregion

    public void Dispose()
    {
        Release();
        label.Dispose();
    }
}
