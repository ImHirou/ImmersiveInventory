using System;
using System.IO;
using HarmonyLib;
using ImmersiveInventory.Commands;
using ImmersiveInventory.Config;
using ImmersiveInventory.Gui;
using ImmersiveInventory.Integration;
using ImmersiveInventory.Masks;
using ImmersiveInventory.Rules;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ImmersiveInventory;

/// <summary>
/// Client-side entry point. The stack on the mouse cursor hangs from the point it was grabbed by and swings like a pendulum;
/// with Inventory Particles its particles follow the turned item and fast swings leave a trail.
/// </summary>
public sealed class ImmersiveInventoryModSystem : ModSystem
{
    public const string Domain = "immersiveinventory";
    public const string BrokenConfigFileName = ImmersiveInventoryConfig.FileName + ".broken";

    /// <summary> Before the GUI (1) and the particle layer inside it: the GUI draws this frame's state. </summary>
    public const double PhysicsRenderOrder = 0.95;

    private readonly RuleRegistry rules = new();
    private readonly ItemMaskCache masks = new();
    private ICoreClientAPI? capi;
    private Harmony? harmony;
    private CursorController? controller;
    private ItemMaskRenderer? maskRenderer;
    private FrameRenderer? physicsRenderer;
    private IParticlesBridge? particles;
    private bool frameErrorLogged;

    internal ImmersiveInventoryConfig Config { get; private set; } = new();

    // After Inventory Particles (0.1), whose API is ready in its StartClientSide.
    public override double ExecuteOrder() => 0.2;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        if (!TryLoadConfig(out var config))
        {
            api.Event.LevelFinalize += () => api.ShowChatMessage(Lang.Get($"{Domain}:config-load-failed", ImmersiveInventoryConfig.FileName, BrokenConfigFileName));
        }

        Config = config;
        rules.Attach(name => api.ClassRegistry.GetBlockClass(name) ?? api.ClassRegistry.GetItemClass(name), Mod.Logger);
        rules.SetDisabled(config.DisabledRules);
        controller = new CursorController(api, rules, masks, config, Mod.Logger);

        harmony = new Harmony(Domain);
        CursorPatches.Install(harmony, controller);

        physicsRenderer = new FrameRenderer(PhysicsRenderOrder, OnFrame);
        maskRenderer = new ItemMaskRenderer(api, masks, Mod.Logger);
        api.Event.RegisterRenderer(physicsRenderer, EnumRenderStage.Ortho, Domain + "-physics");
        api.Event.RegisterRenderer(maskRenderer, EnumRenderStage.Ortho, Domain + "-silhouettes");
        api.Event.LevelFinalize += OnLevelFinalize;

        particles = ParticlesIntegration.TryCreate(api, controller, Mod.Logger);
        ImmersiveInventoryCommands.Register(api, Reload, rules, controller);
    }

    /// <summary> Collectibles and their classes are known once the world is loaded. </summary>
    private void OnLevelFinalize()
    {
        if (capi != null)
        {
            LoadRules();
        }
    }

    private void OnFrame(float deltaTime)
    {
        try
        {
            controller?.Update(deltaTime);
            particles?.OnFrame();
        }
        catch (Exception e)
        {
            if (!frameErrorLogged)
            {
                frameErrorLogged = true;
                Mod.Logger.Error("Immersive Inventory failed in a frame; further errors are not logged: {0}", e);
            }
        }
    }

    private void LoadRules()
    {
        rules.LoadAssets(capi!.Assets, Mod.Logger);
        masks.Clear();
        controller?.Reset();
        Mod.Logger.Notification("Loaded {0} physics rules from assets ({1} from code), {2} active.", rules.AssetCount, rules.CodeCount, rules.Rules.Count);
    }

    public override void Dispose()
    {
        if (capi != null)
        {
            capi.Event.LevelFinalize -= OnLevelFinalize;

            if (physicsRenderer != null)
            {
                capi.Event.UnregisterRenderer(physicsRenderer, EnumRenderStage.Ortho);
            }

            if (maskRenderer != null)
            {
                capi.Event.UnregisterRenderer(maskRenderer, EnumRenderStage.Ortho);
            }
        }

        try
        {
            particles?.Dispose();
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not disconnect from Inventory Particles: {0}", e.Message);
        }

        if (controller != null)
        {
            CursorPatches.Uninstall(controller);
            controller.Dispose();
        }

        harmony?.UnpatchAll(harmony.Id);
        maskRenderer?.Dispose();
        masks.Clear();
        particles = null;
        controller = null;
        maskRenderer = null;
        physicsRenderer = null;
        harmony = null;
        capi = null;
        base.Dispose();
    }

    #region Config

    /// <summary>
    /// Reads the config file. If it cannot be read, a copy is kept as <see cref="BrokenConfigFileName"/>
    /// and false is returned along with the default settings.
    /// </summary>
    private bool TryLoadConfig(out ImmersiveInventoryConfig config)
    {
        ImmersiveInventoryConfig? loaded;

        try
        {
            loaded = capi!.LoadModConfig<ImmersiveInventoryConfig>(ImmersiveInventoryConfig.FileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to read ModConfig/{0}, a copy is kept as {1}: {2}", ImmersiveInventoryConfig.FileName, BrokenConfigFileName, e.Message);
            // Saving the settings later overwrites the file, so keep the user's edits around.
            BackupConfigFile();
            config = new ImmersiveInventoryConfig();
            return false;
        }

        loaded ??= new ImmersiveInventoryConfig();
        loaded.Sanitize();
        loaded.ConfigVersion = ImmersiveInventoryConfig.CurrentVersion;
        // Writes back newly added settings.
        SaveConfig(loaded);
        config = loaded;
        return true;
    }

    private void BackupConfigFile()
    {
        try
        {
            string path = Path.Combine(GamePaths.ModConfig, ImmersiveInventoryConfig.FileName);

            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(GamePaths.ModConfig, BrokenConfigFileName), overwrite: true);
            }
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Failed to copy ModConfig/{0} to {1}: {2}", ImmersiveInventoryConfig.FileName, BrokenConfigFileName, e.Message);
        }
    }

    private void SaveConfig(ImmersiveInventoryConfig config)
    {
        try
        {
            capi!.StoreModConfig(config, ImmersiveInventoryConfig.FileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to save ModConfig/{0}: {1}", ImmersiveInventoryConfig.FileName, e);
        }
    }

    /// <summary> Reads the config file and the rules again. False if the config file is broken. </summary>
    private bool Reload()
    {
        bool configLoaded = TryLoadConfig(out var config);

        if (configLoaded)
        {
            Config = config;
            rules.SetDisabled(config.DisabledRules);
            controller?.ApplyConfig(config);
        }

        if (capi?.World != null)
        {
            LoadRules();
        }

        return configLoaded;
    }

    #endregion
}
