using System.Collections.Generic;

using Avalonia.Media;
using Avalonia.Media.Immutable;

using Rama.Core.Reviews;

using SourceGit.Branding;

namespace SourceGit.Rama
{
    // Pares pálidos (fondo + tinta) por tono semántico; ver docs/DESIGN-SPEC.md.
    public static class ToneBrushes
    {
        private static readonly Dictionary<Color, IBrush> Cache = new();

        public static IBrush Ink(Tone tone) => Brush(PairOf(tone).Ink);
        public static IBrush Background(Tone tone) => Brush(PairOf(tone).Background);

        public static (IBrush Background, IBrush Ink) ForIdentity(string seed)
        {
            var pair = PillPalette.ForIdentity(StableHash(seed));
            return (Brush(pair.Background), Brush(pair.Ink));
        }

        private static PillPalette.Pair PairOf(Tone tone) => tone switch
        {
            Tone.Good => PillPalette.ForLane(Color.Parse("#5cc58a")),
            Tone.Warn => PillPalette.ForLane(Color.Parse("#e6a23c")),
            Tone.Bad => PillPalette.ForLane(Color.Parse("#e0718f")),
            _ => PillPalette.Neutral2,
        };

        private static IBrush Brush(Color c)
        {
            if (!Cache.TryGetValue(c, out var b))
                Cache[c] = b = new ImmutableSolidColorBrush(c);
            return b;
        }

        private static int StableHash(string s)
        {
            var h = 17;
            foreach (var ch in s ?? string.Empty)
                h = unchecked(h * 31 + ch);
            return h;
        }
    }
}
