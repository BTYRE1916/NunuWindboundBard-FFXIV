using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace NunuWindboundBard.Core;

internal sealed unsafe class FollowerCamera
{
    private const float DownwardPitch = -85f * MathF.PI / 180f;
    private Camera* adjustedCamera;
    private CameraValue pitch;
    private CameraValue fieldOfView;

    // Called on the game framework thread. Only the normal world camera is eligible.
    public void Update(bool requested)
    {
        if (!requested && adjustedCamera == null)
            return;

        var manager = CameraManager.Instance();
        if (manager == null || manager->Camera == null)
            return;

        var framework = requested ? GameFramework.Instance() : null;
        bool useLowDetailView = framework != null && framework->WindowInactive &&
            manager->ActiveCameraIndex == 0;
        UpdateCamera(manager->Camera, useLowDetailView);
    }

    internal void UpdateCamera(Camera* camera, bool useLowDetailView)
    {
        if (camera != adjustedCamera)
        {
            // A replaced camera belongs to the game. Never dereference the old pointer.
            adjustedCamera = null;
            pitch = default;
            fieldOfView = default;
        }

        if (!useLowDetailView || !float.IsFinite(camera->DirVMin) ||
            !float.IsFinite(camera->DirVMax) || camera->DirVMin >= camera->DirVMax)
        {
            if (adjustedCamera != null)
            {
                pitch.Restore(ref camera->DirV);
                fieldOfView.Restore(ref camera->FoV);
                adjustedCamera = null;
            }
            return;
        }

        adjustedCamera = camera;
        pitch.Apply(ref camera->DirV, Math.Clamp(DownwardPitch, camera->DirVMin, camera->DirVMax));
        if (float.IsFinite(camera->MinFoV) && camera->MinFoV > 0f && camera->MinFoV <= camera->FoV)
            fieldOfView.Apply(ref camera->FoV, camera->MinFoV);
    }

    private struct CameraValue
    {
        private bool captured;
        private float original;
        private float applied;

        public void Apply(ref float value, float replacement)
        {
            if (!float.IsFinite(value))
                return;

            if (!captured)
                original = value;
            captured = true;
            applied = replacement;
            value = replacement;
        }

        public void Restore(ref float value)
        {
            // Preserve a value changed by the game or another camera tool.
            if (captured && value == applied)
                value = original;
            captured = false;
        }
    }
}
