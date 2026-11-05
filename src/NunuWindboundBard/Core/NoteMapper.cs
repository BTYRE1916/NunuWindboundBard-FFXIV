using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>FFXIV Bard Performance offers 37 semitone steps from C3 (48) through C6 (84).</summary>
public static class NoteMapper
{
    public const int LowestNote = 48;
    public const int HighestNote = 84;
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static string GetNoteName(int pitch) => pitch is >= 0 and <= 127
        ? $"{Names[pitch % 12]}{pitch / 12 - 1}" : pitch.ToString();

    /// <summary>Scores each octave candidate against the 37-key range; ties prefer smaller shifts.</summary>
    public static int FindBestOctaveShift(IEnumerable<int> pitches, int extraTranspose = 0)
    {
        int[] notes = pitches.ToArray();
        if (notes.Length == 0) return 0;
        int best = 0, bestCount = -1;
        double bestPenalty = double.PositiveInfinity;
        foreach (int shift in new[] { 0, 12, -12, 24, -24, 36, -36, 48, -48 })
        {
            int count = 0;
            double center = 0;
            foreach (int note in notes)
            {
                int pitch = note + extraTranspose + shift;
                if (pitch is >= LowestNote and <= HighestNote) count++;
                center += Math.Abs(pitch - 66.0);
            }
            double penalty = Math.Abs(shift) * 1000.0 + center / notes.Length;
            if (count > bestCount || (count == bestCount && penalty < bestPenalty))
            {
                best = shift;
                bestCount = count;
                bestPenalty = penalty;
            }
        }
        return best;
    }
}
