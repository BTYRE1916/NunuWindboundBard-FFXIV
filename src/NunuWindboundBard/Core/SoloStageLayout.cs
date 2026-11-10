using System;
using System.Numerics;

namespace NunuWindboundBard.Core;

/// <summary>A single-Bard stage point. Unlike party formations, it needs no party roster.</summary>
public sealed class SoloStageLayout
{
    public string Name { get; set; } = "My Solo Stage";
    public uint TerritoryId { get; set; }
    public Vector3 Anchor { get; set; }
    public float AnchorHeading { get; set; }
    public Vector3 Offset { get; set; } = new(0f, 0f, 3f);
    public float FacingOffset { get; set; }
    public float WidthMetres { get; set; } = 20f;

    public static SoloStageLayout At(uint territory, Vector3 position, float facing) => new()
    {
        TerritoryId = territory,
        Anchor = position,
        AnchorHeading = StageFormationMath.WrapHeading(facing)
    };

    public bool IsValid() => TerritoryId != 0 && StageFormationMath.Finite(Anchor) &&
        StageFormationMath.Finite(Offset) && float.IsFinite(AnchorHeading) &&
        MathF.Abs(AnchorHeading) < 100f && float.IsFinite(FacingOffset) &&
        MathF.Abs(FacingOffset) < 100f && float.IsFinite(WidthMetres) &&
        WidthMetres is >= 8f and <= 40f &&
        Offset.Length() <= StageFormationMath.MaxCaptureRadius + .01f;

    public void SetSpot(float right, float depth, bool snap)
    {
        if (!float.IsFinite(right) || !float.IsFinite(depth))
            throw new ArgumentOutOfRangeException(nameof(right));
        float half = WidthMetres / 2f;
        right = Math.Clamp(right, -half, half);
        depth = Math.Clamp(depth, -half, half);
        if (snap)
        {
            right = MathF.Round(right * 2f) / 2f;
            depth = MathF.Round(depth * 2f) / 2f;
        }
        float radius = MathF.Sqrt(right * right + depth * depth);
        if (radius > StageFormationMath.MaxCaptureRadius)
        {
            float scale = StageFormationMath.MaxCaptureRadius / radius;
            right *= scale;
            depth *= scale;
        }
        Offset = new Vector3(right, 0f, depth);
    }

    public Vector3 Destination => StageFormationMath.Destination(
        new StageFormationMember { Offset = Offset }, Anchor, AnchorHeading);

    public float Facing => StageFormationMath.WrapHeading(AnchorHeading + FacingOffset);
}
