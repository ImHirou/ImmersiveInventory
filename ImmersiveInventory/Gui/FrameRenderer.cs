using System;
using Vintagestory.API.Client;

namespace ImmersiveInventory.Gui;

/// <summary> Calls back once per frame at its place in the render stage. </summary>
internal sealed class FrameRenderer : IRenderer
{
    private readonly Action<float> onFrame;

    public FrameRenderer(double order, Action<float> onFrame)
    {
        RenderOrder = order;
        this.onFrame = onFrame;
    }

    public double RenderOrder { get; }

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => onFrame(deltaTime);

    public void Dispose()
    {
    }
}
