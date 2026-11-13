using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace NunuWindboundBard;

public sealed class CapturePreviewWindow : Window
{
    private readonly MainWindow owner;

    public CapturePreviewWindow(MainWindow owner)
        : base("Windbound Capture Preview###WindboundCapturePreview", ImGuiWindowFlags.NoScrollbar)
    {
        this.owner = owner;
        Size = new Vector2(1100, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new(480, 320), MaximumSize = new(3840, 2160) };
        IsOpen = false;
    }

    public override void Draw() => owner.DrawStandaloneStudioPreview();
}
