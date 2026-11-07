using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NunuWindboundBard.Core;

/// <summary>A named stage arrangement, relative to the conductor's saved anchor.
/// Saving positions never manipulates the game; moving uses an optional navmesh plugin.</summary>
public sealed class StageFormationPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "My stage";
    public uint TerritoryId { get; set; }
    public Vector3 Anchor { get; set; }
    public float AnchorHeading { get; set; }
    public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
    public List<StageFormationMember> Members { get; set; } = new();
}

public sealed class StageFormationMember
{
    public ulong ContentId { get; set; }
    public string Name { get; set; } = "Bard";
    public Vector3 Offset { get; set; }
    public float HeadingOffset { get; set; }
}

public static class StageFormationMath
{
    public const float MaxCaptureRadius = 32f;
    public const float MaxMoveDistance = 40f;
    public const float ArrivalTolerance = .9f;
    public static float WrapHeading(float heading)
    {
        if (!float.IsFinite(heading)) throw new ArgumentOutOfRangeException(nameof(heading));
        while (heading > MathF.PI) heading -= 2f * MathF.PI;
        while (heading <= -MathF.PI) heading += 2f * MathF.PI;
        return heading;
    }
    public static StageFormationMember Capture(ulong id, string name, Vector3 position,
        float facing, Vector3 anchor, float anchorFacing)
    {
        if (id == 0 || !Finite(position) || !Finite(anchor)) throw new ArgumentException("Invalid member coordinates.");
        if (Vector3.Distance(position, anchor) > MaxCaptureRadius)
            throw new InvalidOperationException("A party member is too far from the stage's saved anchor (32m maximum).");
        float c = MathF.Cos(anchorFacing), s = MathF.Sin(anchorFacing);
        Vector3 delta = position - anchor;
        return new StageFormationMember {
            ContentId = id, Name = name,
            Offset = new Vector3(c * delta.X + s * delta.Z, delta.Y, -s * delta.X + c * delta.Z),
            HeadingOffset = WrapHeading(facing - anchorFacing)
        };
    }
    public static Vector3 Destination(StageFormationMember member, Vector3 anchor, float anchorFacing)
    {
        float c = MathF.Cos(anchorFacing), s = MathF.Sin(anchorFacing);
        Vector3 o = member.Offset;
        return anchor + new Vector3(c * o.X - s * o.Z, o.Y, s * o.X + c * o.Z);
    }
    public static float DesiredHeading(StageFormationMember member, float anchorFacing) =>
        WrapHeading(member.HeadingOffset + anchorFacing);

    public static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        MathF.Abs(value.X) < 100000f && MathF.Abs(value.Y) < 100000f && MathF.Abs(value.Z) < 100000f;

    public static void Validate(StageFormationPreset preset)
    {
        if (preset is null || string.IsNullOrWhiteSpace(preset.Id) ||
            string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 64 || preset.TerritoryId == 0 ||
            !Finite(preset.Anchor) || !float.IsFinite(preset.AnchorHeading) ||
            preset.Members is null || preset.Members.Count is < 2 or > 15 ||
            preset.Members.Select(m => m.ContentId).Distinct().Count() != preset.Members.Count)
            throw new InvalidOperationException("Invalid stage-formation preset.");
        foreach (var member in preset.Members)
        {
            if (member.ContentId == 0 || !Finite(member.Offset) ||
                member.Offset.Length() > MaxCaptureRadius + .1f ||
                !float.IsFinite(member.HeadingOffset))
                throw new InvalidOperationException("Invalid member position in stage formation.");
        }
    }
}
