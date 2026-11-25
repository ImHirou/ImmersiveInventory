using System;
using System.Runtime.CompilerServices;
using ImmersiveInventory.Gui;
using InventoryParticles;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ImmersiveInventory.Integration;

/// <summary> What the mod needs from Inventory Particles, without its types, so that it runs without that mod. </summary>
internal interface IParticlesBridge : IDisposable
{
    /// <summary> Called every frame after the item on the cursor was simulated. </summary>
    void OnFrame();
}

/// <summary>
/// The optional dependency on Inventory Particles. Its types are only touched by <see cref="Create"/> and
/// <see cref="ParticlesBridge"/>, after the mod is known to be enabled, so the assembly is not needed otherwise.
/// </summary>
internal static class ParticlesIntegration
{
    public const string ModId = "inventoryparticles";

    public static IParticlesBridge? TryCreate(ICoreClientAPI capi, CursorController controller, ILogger logger)
    {
        if (!capi.ModLoader.IsModEnabled(ModId))
        {
            return null;
        }

        try
        {
            var bridge = Create(capi, controller, logger);

            if (bridge == null)
            {
                logger.Warning("Inventory Particles is enabled but offers no API; particles don't follow the item on the cursor.");
            }

            return bridge;
        }
        catch (Exception e)
        {
            // An older Inventory Particles without the cursor provider ends up here (MissingMethodException).
            logger.Warning("Inventory Particles could not be connected, particles don't follow the item on the cursor: {0}", e.Message);
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IParticlesBridge? Create(ICoreClientAPI capi, CursorController controller, ILogger logger)
    {
        var api = capi.ModLoader.GetModSystem<InventoryParticlesModSystem>()?.Api;
        return api == null ? null : new ParticlesBridge(api, controller, logger);
    }
}
