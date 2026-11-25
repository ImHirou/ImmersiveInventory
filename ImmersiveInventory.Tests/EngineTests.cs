using System.Numerics;
using System.Reflection;
using HarmonyLib;
using ImmersiveInventory.Config;
using ImmersiveInventory.Gui;
using ImmersiveInventory.Integration;
using ImmersiveInventory.Masks;
using ImmersiveInventory.Rules;
using InventoryParticles.Api;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

/// <summary>
/// Runs whole frames of the controller against a real slot grid with a faked client API, without a window: picking up by a click,
/// the snap, hanging, the drawing in place of the game's, server copies, hiding, swings and the Inventory Particles bridge.
/// </summary>
internal static class EngineTests
{
    private const float Frame = 1f / 60f;

    /// <summary> The torch is in slot 1, whose centre is at 124/24. </summary>
    private static readonly Vector2 TorchSlotCenter = new(124f, 24f);

    public static void Run(string assets)
    {
        RuntimeEnv.GUIScale = 1f;
        var env = new FakeEnvironment(assets);
        var log = new Tests.TestLogger();
        var rules = new RuleRegistry();
        rules.Attach(Tests.ResolveClass, log);
        rules.LoadAssets(env.Assets, log);
        Check(rules.AssetCount == Tests.BundledRules && rules.Rules.Count == Tests.BundledRules && log.Messages.Count == 0,
            $"Rules load through IAsset.ToObject: {rules.AssetCount} rules, {log.Messages.Count} warnings");

        var masks = new ItemMaskCache();
        masks.Store(MaskKey.Of(env.Slots[1].Itemstack!, 1f), TorchMask());
        masks.Store(MaskKey.Of(env.Slots[3].Itemstack!, 1f), SwordMask());
        var controller = new CursorController(env.Api, rules, masks, new ImmersiveInventoryConfig(), log);

        PickUpTorch(env, controller);
        ServerCopiesAndHiding(env, controller);
        Release(env, controller);
        SnapToTheBlade(env, controller);
        SwingAndParticles(env, controller, log);
        VanillaStacks(env, controller, masks);
        HarmonyPatches(env, controller);
        Check(log.Messages.Count == 0, "Nothing went wrong on the way: " + string.Join(" | ", log.Messages));
    }

    /// <summary> The patches go onto the game's real GUI elements and take over only the stack on the cursor. </summary>
    private static void HarmonyPatches(FakeEnvironment env, CursorController controller)
    {
        var harmony = new Harmony("immersiveinventory-tests");

        try
        {
            CursorPatches.Install(harmony, controller);
            var clickInfo = Harmony.GetPatchInfo(AccessTools.Method(typeof(GuiElementItemSlotGridBase), nameof(GuiElementItemSlotGridBase.SlotClick)));
            Check(clickInfo != null && clickInfo.Prefixes.Count(patch => patch.owner == harmony.Id) == 1 && clickInfo.Postfixes.Count(patch => patch.owner == harmony.Id) == 1,
                "The slot click is patched before and after");

            var bounds = ElementBounds.Fixed(0, 0, 48, 48).WithEmptyParent();
            bounds.CalcWorldBounds();
            var cursorElement = new GuiElementPassiveItemSlot(env.Api, bounds, env.Inventory, env.MouseSlot, false);
            var otherElement = new GuiElementPassiveItemSlot(env.Api, bounds, env.Inventory, env.Slots[3], false);

            Click(env, controller, 1, 124, 36);
            controller.Update(Frame);
            env.Calls.Clear();
            cursorElement.RenderInteractiveElements(Frame);
            Check(env.Calls.Any(call => call.Name == "GlPushMatrix") && env.Calls.All(call => call.Name != "PushScissor")
                  && env.Calls.Single(call => call.Name == "RenderItemstackToGui").Args[8] is false,
                "Patched, the passive slot of the cursor is drawn by the mod, without the game's scissor");

            env.Calls.Clear();
            otherElement.RenderInteractiveElements(Frame);
            Check(env.Calls.Any(call => call.Name == "PushScissor") && env.Calls.Single(call => call.Name == "RenderItemstackToGui").Args[8] is true,
                "Any other passive slot is drawn by the game");

            Click(env, controller, 1, 124, 24);
            controller.Update(Frame);
            env.Calls.Clear();
            cursorElement.RenderInteractiveElements(Frame);
            Check(env.Calls.All(call => call.Name != "RenderItemstackToGui"), "An empty cursor slot draws nothing, as in the game");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            CursorPatches.Uninstall(controller);
        }
    }

    /// <summary> A torch: a handle 4 wide from 14 below to 10 above the centre, a head 8 wide above it, a little to the left. </summary>
    private static ItemMask TorchMask() => Mask(80, (-2, -10, 2, 14), (-6, -18, 2, -10));

    /// <summary> A sword from the bottom left (hilt) to the top right (tip), 3 wide. </summary>
    private static ItemMask SwordMask()
    {
        const int size = 80;
        var coverage = new bool[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = x + 0.5f - size / 2f;
                float cy = y + 0.5f - size / 2f;
                // Distance from the line x + y = 0, between (-14, 14) and (14, -14).
                if (MathF.Abs(cx + cy) / MathF.Sqrt(2f) <= 1.5f && cx >= -14f && cx <= 14f)
                {
                    coverage[y * size + x] = true;
                }
            }
        }

        return ItemMask.FromCoverage(coverage, size, 1f);
    }

    private static ItemMask Mask(int size, params (float X0, float Y0, float X1, float Y1)[] boxes)
    {
        var coverage = new bool[size * size];

        foreach (var box in boxes)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float cx = x + 0.5f - size / 2f;
                    float cy = y + 0.5f - size / 2f;

                    if (cx >= box.X0 && cx < box.X1 && cy >= box.Y0 && cy < box.Y1)
                    {
                        coverage[y * size + x] = true;
                    }
                }
            }
        }

        return ItemMask.FromCoverage(coverage, size, 1f);
    }

    private static void Frames(CursorController controller, float seconds)
    {
        for (float t = 0; t < seconds - Frame / 2; t += Frame)
        {
            controller.Update(Frame);
        }
    }

    /// <summary> Clicks a slot the way GuiElementItemSlotGridBase does: SlotClick moves the stacks between the prefix and the postfix. </summary>
    private static void Click(FakeEnvironment env, CursorController controller, int slotId, int mouseX, int mouseY)
    {
        env.MouseX = mouseX;
        env.MouseY = mouseY;
        controller.BeforeSlotClick(env.Grid, slotId);
        (env.MouseSlot.Itemstack, env.Slots[slotId].Itemstack) = (env.Slots[slotId].Itemstack, env.MouseSlot.Itemstack);
        controller.AfterSlotClick();
    }

    /// <summary> Draws the stack on the cursor like HudMouseTools would; false means the game draws it. </summary>
    private static bool Draw(FakeEnvironment env, CursorController controller)
    {
        env.Calls.Clear();
        return controller.RenderCursorStack(env.MouseSlot);
    }

    private static void PickUpTorch(FakeEnvironment env, CursorController controller)
    {
        // Grabbed at the bottom of the handle, 12 below the slot centre.
        Click(env, controller, 1, 124, 36);
        controller.Update(Frame);
        var held = controller.Held;
        Check(held is { HasPhysics: true, Mask: not null } && held.Rule?.Code == "lights" && Tests.Near(held.Grab, new Vector2(0f, 12f)),
            $"A torch picked up by the bottom of its handle hangs from there: rule {held?.Rule?.Code}, grab {held?.Grab}");

        // Drawn by the mod: the grab point on the cursor, the slot centre where the torch was.
        Check(Draw(env, controller), "The mod draws the stack on the cursor");
        Check(env.Calls.Select(call => call.Name).SequenceEqual(new[] { "GlPushMatrix", "GlTranslate", "GlRotate", "RenderItemstackToGui", "GlPopMatrix" }),
            "Push, move to the grab point, turn, draw, pop: " + string.Join(", ", env.Calls.Select(call => call.Name)));
        var translate = env.Calls[1].Args;
        var item = env.Calls[3].Args;
        var drawnCenter = new Vector2(Convert.ToSingle(translate[0]) + Convert.ToSingle(item[1]), Convert.ToSingle(translate[1]) + Convert.ToSingle(item[2]));
        Check(Tests.Near(drawnCenter, TorchSlotCenter, 0.01f) && ReferenceEquals(item[0], env.MouseSlot),
            $"Picked up, the torch is drawn exactly where it was in its slot, at {drawnCenter}");
        Check(Convert.ToDouble(item[3]) == CursorController.CursorDepth && Math.Abs(Convert.ToSingle(item[4]) - 25.6f) < 1e-3f && (int)item[5]! == -1
              && (bool)item[6]! && !(bool)item[7]! && !(bool)item[8]!,
            "At the game's depth and size, shaded, not spinning, without the stack size that would turn along");
        Check(controller.TryGetItemRect(out float x, out float y, out float size, out float rotation) && Math.Abs(x - 124f) < 0.01f && Math.Abs(y - 24f) < 0.01f && size == 48f,
            $"Inventory Particles sees the torch's centre (a block: the slot centre), at {x}/{y}, turned {rotation}");

        // Held still, the head comes down: an upside-down torch hangs from the hand.
        Frames(controller, 5f);
        held = controller.Held!;
        var centerOfMass = held.Body.Turn(held.Body.CenterOffset);
        Check(MathF.Abs(MathF.Abs(held.Body.Angle) - MathF.PI) < 0.1f && centerOfMass.Y > 5f && MathF.Abs(centerOfMass.X) < 0.5f,
            $"A torch held by its handle turns over and hangs head down: angle {held.Body.Angle:0.###}, centre of mass {centerOfMass}");
        Check(controller.TryGetItemRect(out x, out y, out _, out rotation) && Math.Abs(x - 124f) < 1f && Math.Abs(y - 48f) < 1f && MathF.Abs(MathF.Abs(rotation) - MathF.PI) < 0.1f,
            $"The particles see it upside down below the cursor, at {x}/{y}, turned {rotation:0.###}");
        Check(Draw(env, controller) && MathF.Abs(MathF.Abs(Convert.ToSingle(env.Calls[2].Args[0])) - 180f) < 6f, "It is drawn turned by half a turn");
    }

    private static void ServerCopiesAndHiding(FakeEnvironment env, CursorController controller)
    {
        var held = controller.Held!;
        float angle = held.Body.Angle;

        // The server sends its own copy of the stack: the same item keeps swinging.
        env.MouseSlot.Itemstack = env.MouseSlot.Itemstack!.Clone();
        controller.Update(Frame);
        Check(ReferenceEquals(controller.Held, held) && MathF.Abs(held.Body.Angle - angle) < 0.05f, "A copy of the stack from the server doesn't start over");

        // Closing the inventory hides the stack: it freezes and the particles see nothing.
        env.MouseGrabbed = true;
        env.MouseX = 600;
        Frames(controller, 0.5f);
        Check(!held.Visible && !controller.TryGetItemRect(out _, out _, out _, out _), "A hidden stack is not shown to the particles");
        env.MouseGrabbed = false;
        controller.Update(Frame);
        Check(held.Visible && MathF.Abs(held.Body.AngularVelocity) < 1f, $"Shown again far away, it is not thrown: spin {held.Body.AngularVelocity:0.###}");
        env.MouseX = 124;
        Frames(controller, 3f);
    }

    private static void Release(FakeEnvironment env, CursorController controller)
    {
        // Put back into its slot: the cursor is empty and the game draws again.
        Click(env, controller, 1, 124, 24);
        controller.Update(Frame);
        Check(controller.Held == null && env.MouseSlot.Itemstack == null && env.Slots[1].Itemstack != null, "Putting the stack down forgets it");
        Check(!Draw(env, controller) && !controller.TryGetItemRect(out _, out _, out _, out _), "With nothing on the cursor the game draws");
    }

    private static void SnapToTheBlade(FakeEnvironment env, CursorController controller)
    {
        // Clicked into the empty bottom right corner of the sword's slot (slot 3, centre 324/24).
        Click(env, controller, 3, 324 + 16, 24 + 16);
        controller.Update(Frame);
        var held = controller.Held!;
        Check(held.Rule?.Code == "weapons" && held.RawGrab == new Vector2(16f, 16f) && held.Mask!.Covers(held.Grab) && held.Grab.Length() < 4f,
            $"A click next to the blade grabs the nearest point of it: from {held.RawGrab} to {held.Grab}");

        Check(Draw(env, controller), "The sword is drawn by the mod");
        var translate = env.Calls[1].Args;
        var item = env.Calls[3].Args;
        var drawnCenter = new Vector2(Convert.ToSingle(translate[0]) + Convert.ToSingle(item[1]), Convert.ToSingle(translate[1]) + Convert.ToSingle(item[2]));
        Check(Tests.Near(drawnCenter, new Vector2(324f, 24f), 0.6f), $"The snap doesn't make the sword jump: drawn at {drawnCenter}");

        Frames(controller, 0.4f);
        var mouse = new Vector2(env.MouseX, env.MouseY);
        Check(Vector2.Distance(held.Pivot, mouse) < 0.05f, $"Then the grab point glides to the cursor: {Vector2.Distance(held.Pivot, mouse):0.###} away");
    }

    private static void SwingAndParticles(FakeEnvironment env, CursorController controller, Tests.TestLogger log)
    {
        // Put the sword back and take it by the end of its hilt, bottom left.
        Click(env, controller, 3, 324, 24);
        controller.Update(Frame);
        Click(env, controller, 3, 324 - 13, 24 + 13);
        controller.Update(Frame);
        var held = controller.Held!;
        Check(held.Rule?.Code == "weapons" && Tests.Near(held.Grab, new Vector2(-13f, 13f)) && held.Tip.Length() > 35f,
            $"Held by the hilt, the tip is the far end of the blade: grab {held.Grab}, tip {held.Tip}");

        var api = new FakeParticles();
        var bridge = new ParticlesBridge(api.Api, controller, log);
        Check(api.Provider != null, "The bridge tells Inventory Particles where the stack on the cursor is");
        Check(controller.TryGetItemRect(out float x, out float y, out float size, out float rotation) && api.Provider!() is { } rect
              && rect.CenterX == x && rect.CenterY == y && rect.Size == size && rect.Rotation == rotation,
            "The cursor rect Inventory Particles gets is the item as drawn");

        // Let it hang still, then jerk the mouse left and right.
        Frames(controller, 3f);
        bridge.OnFrame();
        Check(api.Plays == 0 && !controller.TryGetSwing(out _, out _), "Hanging still is no swing");
        bool swung = false;

        for (int frame = 0; frame < 120 && !swung; frame++)
        {
            env.MouseX += (frame / 6 % 2 == 0 ? 40 : -40);
            controller.Update(Frame);
            bridge.OnFrame();
            swung = controller.TryGetSwing(out var effect, out _) && effect == new AssetLocation(Tests.Domain, "swing");
        }

        Check(swung && api.Plays == 3 && api.Effects.All(effect => effect == new AssetLocation(Tests.Domain, "swing")),
            $"Jerking the mouse swings the sword: a trail of the swing effect at three points along the blade, {api.Plays} played");
        controller.Update(Frame);
        bridge.OnFrame();
        Check(api.Plays == 3 && api.IntensityChanges > 0, "While it swings the trails only change their strength");

        // Calm again: the trails stop and fade.
        Frames(controller, 3f);
        bridge.OnFrame();
        Check(api.Stops == 3 && api.Alive.Count == 0, $"When the swing is over the trails stop, {api.Stops} stopped");

        bridge.Dispose();
        Check(api.Provider == null, "Disposed, the bridge removes its cursor provider");
        Click(env, controller, 3, 324, 24);
        controller.Update(Frame);
    }

    private static void VanillaStacks(FakeEnvironment env, CursorController controller, ItemMaskCache masks)
    {
        // A stone block and a bucket keep the game's drawing.
        foreach (int slotId in new[] { 0, 2 })
        {
            Click(env, controller, slotId, 24 + slotId * 100, 24);
            controller.Update(Frame);
            Check(controller.Held is { HasPhysics: false } && !Draw(env, controller) && !controller.TryGetItemRect(out _, out _, out _, out _),
                $"{env.MouseSlot.Itemstack?.Collectible.Code} stays as in vanilla");
            Click(env, controller, slotId, 24 + slotId * 100, 24);
            controller.Update(Frame);
        }

        // A stack put on the cursor without a click (a mod's GUI) stays where the game would draw it.
        env.MouseX = 400;
        env.MouseY = 300;
        env.MouseSlot.Itemstack = new ItemStack(Tests.Item("game:stick", 50));
        controller.Update(Frame);
        Check(controller.Held is { HasPhysics: true, Mask: null } held && held.RawGrab == new Vector2(-17f, -17f) && masks.PendingCount == 1,
            "Without a click it hangs from the point under the mouse at the game's own place, and asks for its silhouette");
        Check(Draw(env, controller), "It is drawn by the mod while its silhouette is drawn");
        var translate = env.Calls[1].Args;
        var item = env.Calls[3].Args;
        var drawnCenter = new Vector2(Convert.ToSingle(translate[0]) + Convert.ToSingle(item[1]), Convert.ToSingle(translate[1]) + Convert.ToSingle(item[2]));
        Check(Tests.Near(drawnCenter, new Vector2(417f, 317f)), $"at the game's place, {drawnCenter}");
        Check(MathF.Abs(Convert.ToSingle(env.Calls[2].Args[0])) < 1e-3f, "and doesn't move before it is known");

        // Turned off, the game draws everything.
        controller.ApplyConfig(new ImmersiveInventoryConfig { Enabled = false });
        controller.Update(Frame);
        Check(controller.Held == null && !Draw(env, controller), "Disabled in the config, nothing swings");
        controller.ApplyConfig(new ImmersiveInventoryConfig());
        env.MouseSlot.Itemstack = null;
        controller.Update(Frame);
    }

    private static void Check(bool condition, string label) => Tests.Check(condition, label);

    /// <summary> A recorded call to the render API. </summary>
    internal readonly record struct Call(string Name, object?[] Args);

    private sealed class FakeEnvironment
    {
        public readonly List<Call> Calls = new();
        public readonly DummySlot[] Slots;
        public readonly DummySlot MouseSlot = new();
        public readonly List<IInventory> OpenedInventories = new();
        public readonly GuiElementItemSlotGrid Grid;
        public readonly IInventory Inventory;
        public readonly ICoreClientAPI Api;
        public readonly IAssetManager Assets;
        public int MouseX;
        public int MouseY;
        public bool MouseGrabbed;

        public FakeEnvironment(string assets)
        {
            object? world = null, input = null, render = null, classes = null, gui = null;
            var logger = new Tests.TestLogger();

            Api = (ICoreClientAPI)FakeProxy.Create(typeof(ICoreClientAPI), (method, _) => method.Name switch
            {
                "get_World" => world,
                "get_Input" => input,
                "get_Render" => render,
                "get_Assets" => Assets,
                "get_ClassRegistry" => classes,
                "get_Gui" => gui,
                "get_Logger" => logger,
                _ => FakeProxy.Default(method.ReturnType)
            });

            var icons = new IconUtil(Api);
            var dialogs = new List<GuiDialog>();
            gui = FakeProxy.Create(typeof(IGuiAPI), (method, _) => method.Name switch
            {
                "get_OpenedGuis" => dialogs,
                "get_Icons" => icons,
                _ => FakeProxy.Default(method.ReturnType)
            });

            var inventoryManager = FakeProxy.Create(typeof(IPlayerInventoryManager), (method, _) => method.Name switch
            {
                "get_MouseItemSlot" => MouseSlot,
                "get_OpenedInventories" => OpenedInventories,
                _ => FakeProxy.Default(method.ReturnType)
            });
            var player = FakeProxy.Create(typeof(IClientPlayer), (method, _) => method.Name == "get_InventoryManager" ? inventoryManager : FakeProxy.Default(method.ReturnType));
            world = FakeProxy.Create(typeof(IClientWorldAccessor), (method, _) => method.Name == "get_Player" ? player : FakeProxy.Default(method.ReturnType));
            input = FakeProxy.Create(typeof(IInputAPI), (method, _) => method.Name switch
            {
                "get_MouseX" => MouseX,
                "get_MouseY" => MouseY,
                "get_MouseGrabbed" => MouseGrabbed,
                _ => FakeProxy.Default(method.ReturnType)
            });
            render = FakeProxy.Create(typeof(IRenderAPI), (method, args) =>
            {
                Calls.Add(new Call(method.Name, args ?? Array.Empty<object?>()));
                return FakeProxy.Default(method.ReturnType);
            });
            Assets = (IAssetManager)FakeProxy.Create(typeof(IAssetManager), (method, args) =>
                method.Name == "GetManyInCategory" ? FindAssets(assets, (string)args![0]!, (string)args[1]!) : FakeProxy.Default(method.ReturnType));
            classes = FakeProxy.Create(typeof(IClassRegistryAPI), (method, args) =>
                method.Name is "GetBlockClass" or "GetItemClass" ? Tests.ResolveClass((string)args![0]!) : FakeProxy.Default(method.ReturnType));

            var sword = Tests.Item("game:blade-falx-iron", 20);
            sword.Tool = EnumTool.Sword;
            Slots = new[]
            {
                new DummySlot(new ItemStack(Tests.Block<Block>("game:rock-granite", 20))),
                new DummySlot(new ItemStack(Tests.Block<BlockTorch>("game:torch-basic-lit-up", 21))),
                new DummySlot(new ItemStack(Tests.Block<BlockBucket>("game:woodbucket", 22))),
                new DummySlot(new ItemStack(sword)),
                new DummySlot()
            };
            Inventory = (IInventory)FakeProxy.Create(typeof(IInventory), (method, args) => method.Name switch
            {
                "get_Count" => Slots.Length,
                "get_Item" => Slots[(int)args![0]!],
                _ => FakeProxy.Default(method.ReturnType)
            });
            OpenedInventories.Add(Inventory);

            var viewport = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent();
            var gridBounds = ElementBounds.Fixed(0, 0, 600, 48).WithParent(viewport);
            Grid = new GuiElementItemSlotGrid(Api, Inventory, _ => { }, 5, null!, gridBounds);
            var slotBounds = new[] { 0, 100, 200, 300, 500 }.Select(x => ElementBounds.Fixed(x, 0, 48, 48).WithParent(gridBounds)).ToArray();
            Grid.SlotBounds = slotBounds;
            viewport.CalcWorldBounds();
            gridBounds.CalcWorldBounds();

            foreach (var bounds in slotBounds)
            {
                bounds.CalcWorldBounds();
            }

            Check(Grid.renderedSlots.Count == Slots.Length, "The grid lists its slots");
        }

        private static List<IAsset> FindAssets(string assets, string category, string pathBegins)
        {
            var found = new List<IAsset>();

            foreach (string domainFolder in Directory.GetDirectories(assets))
            {
                string domain = Path.GetFileName(domainFolder);
                string folder = Path.Combine(domainFolder, category, pathBegins);

                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string path = Path.GetRelativePath(domainFolder, file).Replace('\\', '/');
                    found.Add(FakeAsset(new AssetLocation(domain, path), File.ReadAllText(file)));
                }
            }

            return found;
        }

        private static IAsset FakeAsset(AssetLocation location, string text) => (IAsset)FakeProxy.Create(typeof(IAsset), (method, args) =>
        {
            switch (method.Name)
            {
                case "get_Location":
                    return location;
                case "ToText":
                    return text;
                case "ToObject":
                    // IAsset.ToObject<T>(settings) is JsonUtil.ToObject<T>(text, domain, settings).
                    var toObject = typeof(JsonUtil).GetMethod(nameof(JsonUtil.ToObject), new[] { typeof(string), typeof(string), typeof(JsonSerializerSettings) })!;
                    return toObject.MakeGenericMethod(method.GetGenericArguments()).Invoke(null, new[] { text, location.Domain, args?[0] });
                default:
                    return FakeProxy.Default(method.ReturnType);
            }
        });
    }

    /// <summary> Records what the bridge asks of Inventory Particles. </summary>
    private sealed class FakeParticles
    {
        public readonly IInventoryParticlesApi Api;
        public readonly HashSet<long> Alive = new();
        public readonly List<AssetLocation> Effects = new();
        public System.Func<GuiItemRect?>? Provider;
        public int Plays;
        public int Stops;
        public int IntensityChanges;
        private long nextId = 1;

        public FakeParticles()
        {
            Api = (IInventoryParticlesApi)FakeProxy.Create(typeof(IInventoryParticlesApi), (method, args) =>
            {
                switch (method.Name)
                {
                    case "SetCursorRectProvider":
                        Provider = (System.Func<GuiItemRect?>?)args![0];
                        return null;
                    case "Play":
                        Plays++;
                        Effects.Add((AssetLocation)args![0]!);
                        long id = nextId++;
                        Alive.Add(id);
                        return new ParticleHandle(id);
                    case "IsAlive":
                        return Alive.Contains(((ParticleHandle)args![0]!).Id);
                    case "SetIntensity":
                        IntensityChanges++;
                        return Alive.Contains(((ParticleHandle)args![0]!).Id);
                    case "Stop":
                        Stops++;
                        return Alive.Remove(((ParticleHandle)args![0]!).Id);
                    default:
                        return FakeProxy.Default(method.ReturnType);
                }
            });
        }
    }
}
