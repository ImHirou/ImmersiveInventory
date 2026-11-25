using System;
using ImmersiveInventory.Gui;
using InventoryParticles.Api;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Integration;

/// <summary>
/// Tells Inventory Particles where the item on the cursor is and how far it is turned, so that the item's own effects (a torch's
/// flame, flies over rotten food) come out of the right place, and plays the swing effect along the item while it swings.
/// </summary>
internal sealed class ParticlesBridge : IParticlesBridge
{
    /// <summary> Points along the line from the grab point to the tip that leave a trail each. </summary>
    private static readonly float[] TrailFractions = { 0.6f, 0.8f, 1f };

    private readonly IInventoryParticlesApi api;
    private readonly CursorController controller;
    private readonly ILogger logger;
    private readonly ParticleHandle[] trails = new ParticleHandle[TrailFractions.Length];
    private readonly Func<GuiItemRect?>[] trackers = new Func<GuiItemRect?>[TrailFractions.Length];
    private AssetLocation? playing;
    private bool errorLogged;

    public ParticlesBridge(IInventoryParticlesApi api, CursorController controller, ILogger logger)
    {
        this.api = api;
        this.controller = controller;
        this.logger = logger;

        for (int i = 0; i < TrailFractions.Length; i++)
        {
            float fraction = TrailFractions[i];
            trackers[i] = () => controller.TryGetSwingPoint(fraction, out float x, out float y) ? new GuiItemRect(x, y, 0f) : null;
        }

        api.SetCursorRectProvider(CursorRect);
    }

    /// <summary> Null while the game draws the stack on the cursor itself. </summary>
    private GuiItemRect? CursorRect() =>
        controller.TryGetItemRect(out float x, out float y, out float size, out float rotation) ? new GuiItemRect(x, y, size, rotation) : null;

    public void OnFrame()
    {
        try
        {
            UpdateTrails();
        }
        catch (Exception e)
        {
            if (!errorLogged)
            {
                errorLogged = true;
                logger.Error("Swing particles failed; further errors are not logged: {0}", e);
            }

            StopTrails();
        }
    }

    private void UpdateTrails()
    {
        if (!controller.Config.SwingParticles || !controller.TryGetSwing(out var effect, out float intensity) || effect == null)
        {
            StopTrails();
            return;
        }

        if (!effect.Equals(playing))
        {
            StopTrails();
            playing = effect;
        }

        for (int i = 0; i < trails.Length; i++)
        {
            if (api.IsAlive(trails[i]))
            {
                api.SetIntensity(trails[i], intensity);
                continue;
            }

            // Invalid until the world is loaded or for an unknown effect (logged once by Inventory Particles).
            trails[i] = api.Play(effect, ParticleAnchor.Tracked(trackers[i]), new PlayOptions { Intensity = intensity });
        }
    }

    /// <summary> Live particles finish their short life: the trail fades instead of popping. </summary>
    private void StopTrails()
    {
        for (int i = 0; i < trails.Length; i++)
        {
            if (trails[i].IsValid)
            {
                api.Stop(trails[i]);
                trails[i] = ParticleHandle.Invalid;
            }
        }

        playing = null;
    }

    public void Dispose()
    {
        StopTrails();
        api.SetCursorRectProvider(null);
    }
}
