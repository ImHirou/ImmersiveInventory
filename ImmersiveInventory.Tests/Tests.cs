using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using ImmersiveInventory.Config;
using ImmersiveInventory.Gui;
using ImmersiveInventory.Masks;
using ImmersiveInventory.Physics;
using ImmersiveInventory.Rules;
using InventoryParticles.Api;
using InventoryParticles.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using RuleMatch = ImmersiveInventory.Rules.RuleMatch;

internal static class Tests
{
    internal const string Domain = "immersiveinventory";
    internal const int BundledRules = 10;
    private const float Frame = 1f / 60f;

    private static readonly Assembly[] GameAssemblies = { typeof(BlockMeal).Assembly, Assembly.Load("VSEssentials"), typeof(Block).Assembly };

    private static int checks;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        string assets = typeof(Tests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(attribute => attribute.Key == "AssetsDirectory").Value!;

        Pendulum();
        Tracker();
        Swing();
        Snap();
        Silhouettes();
        Grips();
        Grabs();
        Settings();

        var log = new TestLogger();
        var rules = LoadRules(assets, log);
        Check(log.Messages.Count == 0, "The bundled rules know all their classes and tools: " + string.Join(" | ", log.Messages));
        RuleMatching(rules);
        RuleProblems();
        SwingEffect(assets);
        EngineTests.Run(assets);

        return checks;
    }

    #region Physics

    private static PendulumBody Body(Vector2 centerOffset, float gyration = 30f, float damping = 1.8f) => new()
    {
        CenterOffset = centerOffset,
        Gyration = gyration,
        Gravity = 2000f,
        Damping = damping,
        AirDrag = 0.015f,
        MaxSpin = 35f
    };

    private static void Simulate(PendulumBody body, float seconds, Vector2 pivotAcceleration)
    {
        for (float t = 0; t < seconds - Frame / 2; t += Frame)
        {
            body.Advance(Frame, pivotAcceleration);
        }
    }

    private static void Pendulum()
    {
        // From any start, the item comes to hang with its centre of mass straight below the pivot.
        foreach (var offset in new[] { new Vector2(15, 0), new Vector2(1, -15), new Vector2(-10, 10), new Vector2(3, -20), new Vector2(-0.5f, -12) })
        {
            var body = Body(offset);
            Simulate(body, 8f, Vector2.Zero);
            var hanging = body.Turn(body.CenterOffset);
            Check(MathF.Abs(hanging.X) < 0.5f && hanging.Y > 0f && MathF.Abs(body.AngularVelocity) < 0.05f,
                $"An item with its centre of mass at {offset} from the pivot comes to hang below it, at {hanging}");
        }

        // Something held by its head flips over: a torch held at the bottom of its handle ends upside down.
        var torch = Body(new Vector2(-0.3f, -14f));
        Simulate(torch, 6f, Vector2.Zero);
        Check(MathF.Abs(MathF.Abs(torch.Angle) - MathF.PI) < 0.05f, $"An item held below its centre of mass turns upside down, angle {torch.Angle}");

        // Held at the centre of mass, neither gravity nor throws turn it.
        var balanced = Body(Vector2.Zero);

        for (int i = 0; i < 240; i++)
        {
            balanced.Advance(Frame, new Vector2(3000f * MathF.Sin(i * 0.3f), -2000f));
        }

        Check(MathF.Abs(balanced.Angle) < 1e-4f && MathF.Abs(balanced.AngularVelocity) < 1e-4f, "Held at its centre of mass an item doesn't turn");

        // Small swings take 2π √(I / (g L)), the period of a physical pendulum.
        var swinging = Body(new Vector2(0, 15), gyration: 40f, damping: 0f);
        swinging.AirDrag = 0f;
        swinging.SetAngle(0.05f);
        float expected = MathF.Tau * MathF.Sqrt(swinging.Inertia / (swinging.Gravity * 15f));
        double time = 0, firstCrossing = -1, secondCrossing = -1;
        float previous = swinging.Angle;

        while (time < 3 && secondCrossing < 0)
        {
            swinging.Step(PendulumBody.StepSeconds, Vector2.Zero);
            time += PendulumBody.StepSeconds;

            if (previous > 0f && swinging.Angle <= 0f)
            {
                if (firstCrossing < 0)
                {
                    firstCrossing = time;
                }
                else
                {
                    secondCrossing = time;
                }
            }

            previous = swinging.Angle;
        }

        double period = secondCrossing - firstCrossing;
        Check(secondCrossing > 0 && Math.Abs(period - expected) / expected < 0.02, $"Small swings take {period:0.000} s, a physical pendulum {expected:0.000} s");

        // Without damping the energy stays, also in big swings.
        var free = Body(new Vector2(0, 15), gyration: 40f, damping: 0f);
        free.AirDrag = 0f;
        free.SetAngle(2.5f);
        float energy = free.Energy;
        Simulate(free, 2f, Vector2.Zero);
        Check(MathF.Abs(free.Energy - energy) < 0.05f * MathF.Abs(energy), $"Energy is kept without damping: {energy:0} then {free.Energy:0}");

        // Damping takes it away.
        var damped = Body(new Vector2(0, 15), gyration: 40f);
        damped.SetAngle(1.5f);
        energy = damped.Energy;
        Simulate(damped, 1f, Vector2.Zero);
        float after = damped.Energy;
        Check(after < energy - 0.3f * (energy - (-2000f * 15f)), $"Damping calms the swing down: energy {energy:0} then {after:0}");

        // Pulled to the right the item lags behind: its centre of mass swings left.
        var dragged = Body(new Vector2(0, 15));
        Simulate(dragged, 0.1f, new Vector2(4000f, 0f));
        Check(dragged.Turn(dragged.CenterOffset).X < -1f, $"A dragged item lags behind the mouse, centre of mass at {dragged.Turn(dragged.CenterOffset)}");

        // Nonsense input never breaks it.
        var shaken = Body(new Vector2(4, 12));
        var random = new Random(3);

        for (int i = 0; i < 2000; i++)
        {
            var push = new Vector2((float)(random.NextDouble() - 0.5) * 1e6f, (float)(random.NextDouble() - 0.5) * 1e6f);
            shaken.Advance((float)random.NextDouble() * 0.3f, push);
        }

        shaken.Advance(float.NaN, new Vector2(float.NaN, 1f));
        shaken.Advance(Frame, new Vector2(float.PositiveInfinity, 0f));
        Check(float.IsFinite(shaken.Angle) && MathF.Abs(shaken.Angle) <= MathF.PI + 1e-3f && MathF.Abs(shaken.AngularVelocity) <= shaken.MaxSpin,
            $"Huge pushes, long frames and NaN keep the body finite and within its spin limit: {shaken.Angle}, {shaken.AngularVelocity}");

        // The grip spring holds the slot's angle against a mild pull.
        var firm = Body(new Vector2(15, 0));
        firm.Stiffness = 2000f;
        Simulate(firm, 4f, Vector2.Zero);
        Check(MathF.Abs(firm.Angle) < 0.5f, $"A stiff grip keeps the item near its slot angle, at {firm.Angle}");
    }

    private static void Tracker()
    {
        var tracker = new PivotTracker();
        tracker.Reset(Vector2.Zero);
        var position = Vector2.Zero;
        var velocityChange = Vector2.Zero;

        // Moving at 600 units per second: the accelerations add up to that speed and die out once it is reached.
        for (int i = 0; i < 60; i++)
        {
            position.X += 10f;
            tracker.Update(position, Frame);
            velocityChange += tracker.Acceleration * Frame;
        }

        Check(MathF.Abs(tracker.Acceleration.X) < 1f && MathF.Abs(velocityChange.X - 600f) < 1f && MathF.Abs(tracker.Velocity.X - 600f) < 1f,
            $"Steady motion: speed {tracker.Velocity.X:0}, summed acceleration {velocityChange.X:0}, now {tracker.Acceleration.X:0.##}");

        tracker.Update(position + new Vector2(1000f, 0f), Frame);
        Check(tracker.Acceleration == Vector2.Zero && tracker.Velocity == Vector2.Zero, "A jump of the cursor gives no kick");

        tracker.Update(position + new Vector2(1300f, 0f), 1e-4f);
        Check(tracker.Acceleration.Length() <= tracker.MaxAcceleration + 1f, "Spikes are limited");

        tracker.Update(new Vector2(float.NaN, 0f), Frame);
        Check(tracker.Acceleration == Vector2.Zero, "NaN starts the tracker over");
    }

    private static void Swing()
    {
        var swing = new SwingDetector { Threshold = 400f };
        swing.Update(300f, Frame);
        Check(!swing.Active && swing.Intensity == 0f, "Slow turns are no swing");
        swing.Update(500f, Frame);
        Check(swing.Active && MathF.Abs(swing.Intensity - 1.25f) < 1e-4f, "A fast tip starts a swing, as strong as it is fast");
        swing.Update(300f, Frame);
        Check(swing.Active, "Slowing down a little keeps it going");
        swing.Update(100f, 0.03f);
        Check(swing.Active, "A short dip keeps it going");
        swing.Update(100f, 0.03f);
        Check(!swing.Active && swing.Intensity == 0f, "Staying slow ends it");
        swing.Update(float.NaN, Frame);
        Check(!swing.Active, "NaN is no swing");
    }

    private static void Snap()
    {
        var snap = new SnapSpring();
        var start = new Vector2(10f, -4f);
        snap.Start(start, 1.0, 0.06f);
        Check(snap.Offset(1.0) == start && snap.Offset(0.5) == start, "The snap starts at the whole offset");
        float last = start.Length();

        foreach (double time in new[] { 1.01, 1.02, 1.03, 1.04, 1.05 })
        {
            float length = snap.Offset(time).Length();
            Check(length < last, $"The offset shrinks steadily, {length:0.###} at {time}");
            last = length;
        }

        Check(snap.Offset(1.06).Length() < 0.1f * start.Length() && snap.Offset(2.0) == Vector2.Zero, "Most of the way is done after the snap time, all of it soon after");
        Check(default(SnapSpring).Offset(5.0) == Vector2.Zero, "No snap, no offset");
    }

    #endregion

    #region Silhouettes

    /// <summary> Covers the pixels whose centres lie in the rectangle (GUI units). </summary>
    private static void Fill(bool[] coverage, int size, float scale, float x0, float y0, float x1, float y1)
    {
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float cx = (x + 0.5f - size / 2f) / scale;
                float cy = (y + 0.5f - size / 2f) / scale;

                if (cx >= x0 && cx < x1 && cy >= y0 && cy < y1)
                {
                    coverage[y * size + x] = true;
                }
            }
        }
    }

    private static void Silhouettes()
    {
        // A stick 4 wide and 40 tall with a 12 × 8 head on top, drawn at GUI scale 2 (a pixel is half a unit).
        const int size = 128;
        const float scale = 2f;
        var coverage = new bool[size * size];
        Fill(coverage, size, scale, -2, -20, 2, 20);
        Fill(coverage, size, scale, -6, -28, 6, -20);
        var mask = ItemMask.FromCoverage(coverage, size, scale);

        Check(mask.Area == (4 * 40 + 12 * 8) * 4, $"The silhouette has {mask.Area} pixels");
        Check(mask.Covers(Vector2.Zero) && mask.Covers(new Vector2(0, -25)) && !mask.Covers(new Vector2(5, 0)) && !mask.Covers(new Vector2(500, 0)), "Points on and off the item");
        Check(mask.Nearest(new Vector2(0.5f, 3f)) == new Vector2(0.5f, 3f), "A grab on the item stays where it is");
        var snapped = mask.Nearest(new Vector2(10f, 5f));
        Check(MathF.Abs(snapped.X - 1.75f) < 1e-4f && MathF.Abs(snapped.Y - 5f) <= 0.25f && mask.Covers(snapped), $"A grab next to the item snaps to its edge, at {snapped}");
        snapped = mask.Nearest(new Vector2(-30f, -40f));
        Check(mask.Covers(snapped) && MathF.Abs(snapped.X + 5.75f) < 1e-4f && MathF.Abs(snapped.Y + 27.75f) < 1e-4f, $"Far away it snaps to the nearest corner, at {snapped}");

        // Centre of mass: the stick (160) at 0, the head (96) at -24.
        Check(MathF.Abs(mask.Centroid.X) < 1e-3f && MathF.Abs(mask.Centroid.Y + 9f) < 1e-3f, $"The centre of mass is between stick and head, at {mask.Centroid}");
        double sum = 0;
        int count = 0;

        for (int i = 0; i < coverage.Length; i++)
        {
            if (!coverage[i])
            {
                continue;
            }

            var center = new Vector2((i % size + 0.5f - size / 2f) / scale, (i / size + 0.5f - size / 2f) / scale);
            sum += Vector2.DistanceSquared(center, mask.Centroid);
            count++;
        }

        float gyration = (float)(sum / count) + 1f / (scale * scale) / 6f;
        Check(MathF.Abs(mask.GyrationSquared - gyration) < 1e-2f, $"The moment of inertia is that of the pixels, {mask.GyrationSquared} vs {gyration}");
        var tip = mask.Farthest(new Vector2(0f, 19f));
        Check(MathF.Abs(tip.Y + 27.75f) < 1e-4f, $"Held at the bottom of the stick, the tip is the top of the head, at {tip}");

        // From RGBA: only opaque enough pixels count, the first row is the top.
        var rgba = new byte[4 * 4 * 4];
        rgba[(0 * 4 + 1) * 4 + 3] = 255;
        rgba[(3 * 4 + 2) * 4 + 3] = 30;
        var small = ItemMask.FromRgba(rgba, 4, 1f);
        Check(small.Area == 1 && small.Covers(new Vector2(-0.5f, -1.5f)) && !small.Covers(new Vector2(0.5f, 1.5f)), "Pixels at least a quarter opaque belong to the item, rows from the top");

        var disc = ItemMask.Disc(10f, 1f);
        Check(Math.Abs(disc.Area - MathF.PI * 100f) < 0.05f * MathF.PI * 100f && disc.Centroid.Length() < 0.1f, $"The stand-in is a disc of {disc.Area} pixels");

        var empty = ItemMask.FromCoverage(new bool[16], 4, 1f);
        Check(empty.IsEmpty && empty.Nearest(new Vector2(3, 3)) == new Vector2(3, 3) && empty.GyrationSquared > 0f, "An invisible item keeps the grab point and some inertia");
        Check(ItemMaskRenderer.SizeFor(25.6f) == 80 && ItemMaskRenderer.SizeFor(51.2f) == 160 && ItemMaskRenderer.SizeFor(1000f) == 1024, "The drawn square covers three item sizes");
    }

    private static void Grips()
    {
        var itemCenter = new Vector2(-3f, -1f);
        // Half a slot of handle, straight through the item's centre.
        var handle = new[] { new GripZone { Line = new[] { 0f, -0.25f, 0f, 0.25f } } };
        var nearest = GripGeometry.Nearest(handle, new Vector2(10f, 0f), itemCenter);
        Check(Near(nearest, new Vector2(-3f, 0f)), $"A grab next to a grip line moves onto it, at {nearest}");
        nearest = GripGeometry.Nearest(handle, new Vector2(0f, -30f), itemCenter);
        Check(Near(nearest, new Vector2(-3f, -13f)), $"Past the end of the line it takes the end (+Y is up), at {nearest}");

        var box = new[] { new GripZone { Rect = new[] { 0f, 0f, 0.5f, 0.25f } } };
        Check(Near(GripGeometry.Nearest(box, new Vector2(20f, 20f), itemCenter), new Vector2(9f, 5f)), "Outside a grip box it takes the nearest point of the box");
        Check(Near(GripGeometry.Nearest(box, new Vector2(0f, 0f), itemCenter), Vector2.Zero), "Inside a grip box the grab stays");

        var both = new[] { new GripZone { Point = new[] { 0.5f, 0.5f } }, new GripZone { Point = new[] { -0.5f, -0.5f } } };
        Check(Near(GripGeometry.Nearest(both, new Vector2(30f, -30f), itemCenter), new Vector2(21f, -25f)), "The nearest of several zones wins");
        Check(GripGeometry.Nearest(new[] { new GripZone() }, new Vector2(1f, 2f), itemCenter) == new Vector2(1f, 2f), "Empty zones change nothing");
    }

    private static void Grabs()
    {
        var stickItem = Item("game:stick", 1);
        var flintItem = Item("game:flint", 2);
        var stick = new ItemStack(stickItem);
        var flint = new ItemStack(flintItem);
        var grabs = new GrabCapture();

        grabs.Before(null, new Vector2(110f, 30f), new Vector2(124f, 24f));
        grabs.After(stick, 1.0);
        Check(grabs.TryTake(stickItem, 1.01, out var grab) && grab.Offset == new Vector2(-14f, 6f), "Picking a stack up remembers where on its slot it was grabbed");
        Check(!grabs.TryTake(stickItem, 1.02, out _), "A grab is used once");

        grabs.Before(stick, Vector2.Zero, Vector2.Zero);
        grabs.After(stick, 2.0);
        Check(grabs.Pending == null, "Putting some items down keeps the stack on the cursor, no new grab");

        grabs.Before(stick, new Vector2(5f, 5f), Vector2.Zero);
        grabs.After(flint, 3.0);
        Check(grabs.TryTake(flintItem, 3.0, out grab) && grab.Offset == new Vector2(5f, 5f), "A swap grabs the other stack where the click was");

        grabs.Before(null, Vector2.Zero, Vector2.Zero);
        grabs.After(stick, 4.0);
        Check(!grabs.TryTake(flintItem, 4.0, out _) && grabs.Pending != null, "A grab only explains its own item");
        Check(!grabs.TryTake(stickItem, 4.0 + GrabCapture.MaxAgeSeconds + 0.1, out _) && grabs.Pending == null, "An old grab is forgotten");

        grabs.After(stick, 5.0);
        Check(grabs.Pending == null, "Without a click there is no grab");
        grabs.Before(null, Vector2.Zero, Vector2.Zero);
        grabs.Disarm();
        grabs.After(stick, 6.0);
        Check(grabs.Pending == null, "A click that can't be measured makes no grab");
    }

    private static void Settings()
    {
        var config = new ImmersiveInventoryConfig { Gravity = float.NaN, Damping = -1f, MaxSpin = 0f, SnapTime = 99f, DisabledRules = new List<string> { " items ", "", "ITEMS", "weapons" } };
        config.Sanitize();
        Check(config.Gravity == 2000f && config.Damping == 0f && config.MaxSpin == 1f && config.SnapTime == 2f && config.DisabledRules.SequenceEqual(new[] { "items", "weapons" }),
            "Broken settings fall back or are clamped, disabled rules are cleaned up");
    }

    #endregion

    #region Rules

    internal static Type? ResolveClass(string name) => GameAssemblies
        .Select(assembly => assembly.GetType("Vintagestory.GameContent." + name) ?? assembly.GetType("Vintagestory.API.Common." + name))
        .FirstOrDefault(type => type != null);

    internal static Item Item(string code, int id) => new() { Code = new AssetLocation(code), ItemId = id };

    internal static T Block<T>(string code, int id) where T : Block, new() => new() { Code = new AssetLocation(code), BlockId = id };

    private static RuleRegistry LoadRules(string assets, TestLogger log)
    {
        var registry = new RuleRegistry();
        registry.Attach(ResolveClass, log);
        string file = Path.Combine(assets, Domain, "config", "immersiveinventory", "rules", "vanilla.json");
        // The same deserializer IAsset.ToObject uses.
        var rules = JsonUtil.ToObject<PhysicsRule[]>(File.ReadAllText(file), Domain);
        Check(rules is { Length: BundledRules }, $"The bundled file has {rules?.Length} rules");

        foreach (var rule in rules!)
        {
            rule.Domain = Domain;
            registry.Register(rule);
        }

        Check(registry.Rules.Count == BundledRules, "All bundled rules are active");
        return registry;
    }

    private static void Expect(RuleRegistry rules, CollectibleObject collectible, string rule, bool physics)
    {
        var picked = rules.Pick(collectible);
        Check(picked?.Code == rule && (picked.Profile != null) == physics,
            $"{collectible.Code} ({collectible.GetType().Name}) goes by rule {rule}, {(physics ? "swings" : "no physics")}: got {picked?.Code}, {(picked?.Profile != null ? "swings" : "no physics")}");
    }

    private static void RuleMatching(RuleRegistry rules)
    {
        var order = rules.Rules.Select(rule => rule.Source.Priority).ToArray();
        Check(order.SequenceEqual(order.OrderByDescending(priority => priority)), "Rules are checked by descending priority");

        Expect(rules, Item("game:stick", 1), "items", true);
        Expect(rules, Item("game:clothes-upperbody-tunic", 2), "items", true);
        Expect(rules, Block<Block>("game:rock-granite", 1), "blocks", false);
        Expect(rules, Block<Block>("game:planks-oak-hor", 2), "blocks", false);
        Expect(rules, Block<BlockTorch>("game:torch-basic-lit-up", 3), "lights", true);
        Expect(rules, Block<BlockLantern>("game:lantern-up", 4), "lights", true);
        Expect(rules, Block<BlockOilLamp>("game:oillamp-up", 5), "lights", true);
        Expect(rules, Block<Block>("game:paperlantern-on", 6), "paperlanterns", true);
        Expect(rules, Block<BlockBucket>("game:woodbucket", 7), "containers", false);
        Expect(rules, Block<BlockMeal>("game:bowl-meal", 8), "containers", false);
        Expect(rules, Block<BlockCookingContainer>("game:claypot-blue-cooked", 9), "containers", false);
        Expect(rules, Block<BlockCrock>("game:crock-burned", 10), "containers", false);
        Expect(rules, Block<BlockLiquidContainerTopOpened>("game:jug-blue-fired", 11), "containers", false);
        // The class is internal to the game; make one the way the registry does.
        var portion = (Item)Activator.CreateInstance(ResolveClass("ItemLiquidPortion")!, true)!;
        portion.Code = new AssetLocation("game:waterportion");
        portion.ItemId = 3;
        Expect(rules, portion, "liquids", false);

        var sword = Item("game:blade-falx-iron", 4);
        sword.Tool = EnumTool.Sword;
        Expect(rules, sword, "weapons", true);
        Check(rules.Resolve(sword)?.Swing == new AssetLocation(Domain, "swing"), "Swords have the swing effect of this mod");
        var pickaxe = Item("game:pickaxe-iron", 5);
        pickaxe.Tool = EnumTool.Pickaxe;
        Expect(rules, pickaxe, "items", true);
        Check(rules.Resolve(pickaxe)?.Swing == null, "Other tools swing without particles");

        var apple = Item("game:fruit-redapple", 6);
        apple.NutritionProps = new FoodNutritionProperties();
        Expect(rules, apple, "items", true);
        var pumpkin = Block<Block>("game:pumpkin-fruit-4", 12);
        pumpkin.NutritionProps = new FoodNutritionProperties();
        Expect(rules, pumpkin, "food-blocks", true);
        var mushroom = Block<BlockMushroom>("game:mushroom-bolete-normal", 13);
        mushroom.NutritionProps = new FoodNutritionProperties();
        Expect(rules, mushroom, "food-blocks", true);
        Expect(rules, Block<BlockPlant>("game:flower-catmint-free", 14), "plants", true);
        Expect(rules, Block<BlockSapling>("game:sapling-oak-free", 15), "plants", true);
        var weed = Block<Block>("mymod:weed", 16);
        weed.DrawType = EnumDrawType.Cross;
        Expect(rules, weed, "cross-blocks", true);
    }

    private static void RuleProblems()
    {
        var log = new TestLogger();
        var registry = new RuleRegistry();
        registry.Attach(ResolveClass, log);
        registry.Register(new PhysicsRule { Code = "unknownclass", Priority = 5, Match = new RuleMatch { Classes = new[] { "BlockDoesNotExist" } } });
        registry.Register(new PhysicsRule { Code = "unknowntool", Priority = 5, Match = new RuleMatch { Tools = new[] { "lightsaber" } } });
        registry.Register(new PhysicsRule { Code = "badtype", Priority = 5, Match = new RuleMatch { Type = "fluid" } });
        registry.Register(new PhysicsRule { Code = "fallback", Priority = 0, Physics = false });
        Check(registry.Rules.Count == 1 && log.Messages.Count == 3, $"Rules with conditions that can't be understood are left out and logged: {registry.Rules.Count} active, {log.Messages.Count} warnings");
        Check(registry.Pick(Item("game:stick", 1))?.Code == "fallback", "and never match everything");

        var ordered = new RuleRegistry();
        ordered.Attach(ResolveClass, log);
        ordered.Register(new PhysicsRule { Code = "low", Priority = 1, Match = new RuleMatch { Type = "item" } });
        ordered.Register(new PhysicsRule { Code = "high", Priority = 9, Match = new RuleMatch { Type = "item" }, Gravity = float.NaN, Damping = -2f, Inertia = 3f });
        Check(ordered.Pick(Item("game:stick", 1))?.Code == "high", "The highest priority wins, whatever the order of registration");
        var profile = ordered.Resolve(Item("game:flint", 2))!;
        Check(profile.Gravity == 1f && profile.Damping == 1f && profile.Inertia == 3f, "Broken multipliers fall back to 1");
        ordered.SetDisabled(new[] { "HIGH" });
        Check(ordered.Pick(Item("game:stick", 1))?.Code == "low", "Disabled rules are skipped, whatever their case");
    }

    private static void SwingEffect(string assets)
    {
        string file = Path.Combine(assets, Domain, "config", "inventoryparticles", "effects", "swing.json");
        var effect = JsonUtil.ToObject<GuiParticleEffect>(File.ReadAllText(file), Domain);
        Check(effect?.Emitters is { Length: 2 } && effect.Emitters.All(emitter =>
                emitter.LifeLength.avg > 0f && emitter.Size.avg > 0f && emitter.Rate.avg > 0f && emitter.Layer == EnumParticleLayer.Top && emitter.Blend == EnumGuiParticleBlend.Glow),
            "The swing effect is a short glowing trail above the GUI");
    }

    #endregion

    internal static void Check(bool condition, string label)
    {
        checks++;

        if (!condition)
        {
            throw new Exception("Check failed: " + label);
        }
    }

    internal static bool Near(Vector2 a, Vector2 b, float tolerance = 1e-3f) => Vector2.Distance(a, b) <= tolerance;

    internal sealed class TestLogger : LoggerBase
    {
        public List<string> Messages { get; } = new();

        protected override void LogImpl(EnumLogType logType, string format, params object[] args)
        {
            if (logType is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            {
                Messages.Add(args is { Length: > 0 } ? string.Format(format, args) : format);
            }
        }
    }
}
