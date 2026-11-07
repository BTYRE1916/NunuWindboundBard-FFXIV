using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace NunuWindboundBard.Core;

/// <summary>
/// Pure drawing-independent editor for top-down stage dots. X is stage right,
/// negative Z is the top of the editor. The conductor's anchor and heading are
/// used by the existing, validated StageFormationMath.Destination transform.
/// Editing a draft never moves a character or mutates the saved formation.
/// </summary>
public enum VisualStageTemplate { TwoRows, Line, Arc, Circle }

public sealed class VisualStageDesigner
{
    public string SourcePresetId { get; set; } = "";
    public string Name { get; set; } = "Visual Octet Stage";
    public uint TerritoryId { get; set; }
    public Vector3 Anchor { get; private set; }
    public float AnchorHeading { get; private set; }
    public float StageWidthMetres { get; private set; } = 20f;
    public List<StageFormationMember> Members { get; } = new();

    public static VisualStageDesigner New(string name, uint territory, Vector3 anchor, float heading,
        IReadOnlyList<(ulong Id, string Name)> roster)
    {
        if (roster.Count is < 2 or > 15 || roster.Any(r => r.Id == 0) ||
            roster.Select(r => r.Id).Distinct().Count() != roster.Count ||
            !StageFormationMath.Finite(anchor) || !float.IsFinite(heading) || territory == 0)
            throw new InvalidOperationException("A visual stage requires 2–15 unique performers, a valid zone and anchor.");
        var draft = new VisualStageDesigner { Name = name, TerritoryId = territory,
            Anchor = anchor, AnchorHeading = StageFormationMath.WrapHeading(heading) };
        draft.CreateRows(roster);
        return draft;
    }

    public static VisualStageDesigner FromPreset(StageFormationPreset preset)
    {
        StageFormationMath.Validate(preset);
        var editor = new VisualStageDesigner {
            Name = preset.Name, SourcePresetId = preset.Id,
            TerritoryId = preset.TerritoryId, Anchor = preset.Anchor,
            AnchorHeading = preset.AnchorHeading
        };
        foreach (var member in preset.Members)
            editor.Members.Add(Clone(member));
        float extent = editor.Members.Max(m => MathF.Max(MathF.Abs(m.Offset.X), MathF.Abs(m.Offset.Z)));
        editor.StageWidthMetres = Math.Clamp(MathF.Ceiling((extent + 2f) * 2f), 8f, 64f);
        return editor;
    }

    private void CreateRows(IReadOnlyList<(ulong Id, string Name)> roster)
    {
        Members.Clear();
        // Up to 8 across, then a second row: suitable for octet concerts without
        // putting performers several metres apart by default.
        int columns = Math.Min(8, roster.Count);
        int rows = (roster.Count + columns - 1) / columns;
        for (int index = 0; index < roster.Count; index++)
        {
            int row = index / columns;
            int inRow = Math.Min(columns, roster.Count - row * columns);
            int col = index % columns;
            float x = (col - (inRow - 1) / 2f) * 2f;
            float z = (row - (rows - 1) / 2f) * 2.6f;
            Members.Add(new StageFormationMember {
                ContentId = roster[index].Id, Name = roster[index].Name,
                Offset = new Vector3(x, 0f, z), HeadingOffset = 0f
            });
        }
    }

    public void ApplyTemplate(VisualStageTemplate template)
    {
        int count = Members.Count;
        if (count is < 2 or > 15) throw new InvalidOperationException("No Bards to arrange.");
        float half = StageWidthMetres / 2f - 1.5f;
        for (int i = 0; i < count; i++)
        {
            float x;
            float z;
            if (template == VisualStageTemplate.Line)
            {
                x = (i / (float)(count - 1) - .5f) * MathF.Min(half * 2f, count * 1.9f);
                z = 0f;
            }
            else if (template == VisualStageTemplate.Arc)
            {
                float angle = MathF.PI * (.15f + .70f * i / (count - 1));
                x = MathF.Cos(angle) * half * .84f;
                z = -MathF.Sin(angle) * half * .55f + half * .24f;
            }
            else if (template == VisualStageTemplate.Circle)
            {
                float angle = 2f * MathF.PI * i / count;
                x = MathF.Cos(angle) * half * .78f;
                z = MathF.Sin(angle) * half * .78f;
            }
            else
            {
                int columns = Math.Min(8, count);
                int rows = (count + columns - 1) / columns;
                int row = i / columns;
                int inRow = Math.Min(columns, count - row * columns);
                int col = i % columns;
                x = (col - (inRow - 1) / 2f) * MathF.Min(2f, half * 2f / Math.Max(1, columns));
                z = (row - (rows - 1) / 2f) * 2.6f;
            }
            MoveDot(i, x, z, false);
        }
    }

    public void SetWidth(float width)
    {
        if (!float.IsFinite(width)) throw new ArgumentOutOfRangeException(nameof(width));
        StageWidthMetres = Math.Clamp(width, 8f, 64f);
        float half = StageWidthMetres / 2f;
        foreach (var m in Members)
            m.Offset = new Vector3(Math.Clamp(m.Offset.X, -half, half), m.Offset.Y,
                Math.Clamp(m.Offset.Z, -half, half));
    }

    public void MoveDot(int index, float x, float z, bool snapHalfMetre)
    {
        if (index < 0 || index >= Members.Count || !float.IsFinite(x) || !float.IsFinite(z))
            throw new ArgumentOutOfRangeException(nameof(index));
        float half = StageWidthMetres / 2f;
        x = Math.Clamp(x, -half, half);
        z = Math.Clamp(z, -half, half);
        if (snapHalfMetre)
        {
            x = MathF.Round(x * 2f) / 2f;
            z = MathF.Round(z * 2f) / 2f;
        }
        var m = Members[index];
        // A wide canvas may show spots beyond the existing v0.30 capture limit.
        // Never let the editor save a target outside the validated 32m radius.
        float radialLimit = MathF.Sqrt(MathF.Max(0f,
            StageFormationMath.MaxCaptureRadius * StageFormationMath.MaxCaptureRadius - m.Offset.Y * m.Offset.Y));
        float horizontal = MathF.Sqrt(x * x + z * z);
        if (horizontal > radialLimit && horizontal > 0f)
        {
            float scale = radialLimit / horizontal;
            x *= scale;
            z *= scale;
        }
        m.Offset = new Vector3(x, m.Offset.Y, z);
    }

    public void AssignCharacter(int spotIndex, ulong contentId)
    {
        if (spotIndex < 0 || spotIndex >= Members.Count || contentId == 0)
            throw new ArgumentOutOfRangeException(nameof(spotIndex));
        int other = Members.FindIndex(m => m.ContentId == contentId);
        if (other < 0) throw new InvalidOperationException("That Bard isn't part of the stage's roster.");
        if (other == spotIndex) return;
        // Swapping the performers keeps all spots uniquely assigned even in an octet.
        (Members[spotIndex].ContentId, Members[other].ContentId) =
            (Members[other].ContentId, Members[spotIndex].ContentId);
        (Members[spotIndex].Name, Members[other].Name) = (Members[other].Name, Members[spotIndex].Name);
    }

    public void SetHeading(int spotIndex, float radians)
    {
        if (spotIndex < 0 || spotIndex >= Members.Count) throw new ArgumentOutOfRangeException(nameof(spotIndex));
        Members[spotIndex].HeadingOffset = StageFormationMath.WrapHeading(radians);
    }

    public void Reanchor(uint territory, Vector3 anchor, float heading)
    {
        if (territory == 0 || !StageFormationMath.Finite(anchor) || !float.IsFinite(heading))
            throw new InvalidOperationException("Cannot anchor to the current character position.");
        TerritoryId = territory;
        Anchor = anchor;
        AnchorHeading = StageFormationMath.WrapHeading(heading);
    }

    public bool MatchesRoster(IReadOnlyList<ulong> contentIds) =>
        Members.Count == contentIds.Count &&
        Members.Select(m => m.ContentId).OrderBy(id => id)
            .SequenceEqual(contentIds.OrderBy(id => id));

    public StageFormationPreset Build(string id = "")
    {
        var preset = new StageFormationPreset {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = Name.Trim(), TerritoryId = TerritoryId, Anchor = Anchor,
            AnchorHeading = AnchorHeading, SavedUtc = DateTime.UtcNow,
            Members = Members.Select(Clone).ToList()
        };
        StageFormationMath.Validate(preset);
        return preset;
    }

    public Vector2 ToCanvas(Vector3 offset, float sidePixels)
    {
        float half = StageWidthMetres / 2f;
        return new Vector2((offset.X + half) / StageWidthMetres * sidePixels,
            (offset.Z + half) / StageWidthMetres * sidePixels);
    }

    public Vector2 FromCanvas(Vector2 pointPixels, float sidePixels)
    {
        if (sidePixels <= 0 || !float.IsFinite(sidePixels) || !float.IsFinite(pointPixels.X) ||
            !float.IsFinite(pointPixels.Y)) throw new ArgumentOutOfRangeException(nameof(sidePixels));
        float half = StageWidthMetres / 2f;
        return new Vector2(Math.Clamp(pointPixels.X / sidePixels, 0f, 1f) * StageWidthMetres - half,
            Math.Clamp(pointPixels.Y / sidePixels, 0f, 1f) * StageWidthMetres - half);
    }

    private static StageFormationMember Clone(StageFormationMember m) => new() {
        ContentId = m.ContentId, Name = m.Name, Offset = m.Offset, HeadingOffset = m.HeadingOffset
    };
}
