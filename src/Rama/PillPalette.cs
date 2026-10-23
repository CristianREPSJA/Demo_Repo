using Avalonia.Media;

namespace SourceGit.Branding
{
    // Pares palidos (fondo + tinta) por color de carril; ver docs/DESIGN-SPEC.md.
    public static class PillPalette
    {
        public readonly record struct Pair(Color Background, Color Ink);

        private static readonly (Color Lane, Pair Pair)[] Pairs =
        [
            (Color.Parse("#5aa9e6"), new Pair(Color.Parse("#1b2c3b"), Color.Parse("#8ec5ef"))),
            (Color.Parse("#5cc58a"), new Pair(Color.Parse("#1c3326"), Color.Parse("#8fdcae"))),
            (Color.Parse("#e6a23c"), new Pair(Color.Parse("#3a2f1a"), Color.Parse("#f0c27a"))),
            (Color.Parse("#e0718f"), new Pair(Color.Parse("#36202a"), Color.Parse("#ee9ab1"))),
            (Color.Parse("#b294e6"), new Pair(Color.Parse("#2a2438"), Color.Parse("#c9b3f0"))),
        ];

        private static readonly Pair Slate = new(Color.Parse("#22293a"), Color.Parse("#a9bff0"));
        private static readonly Pair Neutral = new(Color.Parse("#2c2e33"), Color.Parse("#a9abb1"));

        public static Pair Neutral2 => Neutral;

        // Para el color de un carril devuelve su par; si no es de los 5 base, deriva uno.
        public static Pair ForLane(Color lane)
        {
            foreach (var (c, p) in Pairs)
                if (c == lane)
                    return p;

            return Derive(lane);
        }

        // Para identidades (avatares): par estable segun hash.
        public static Pair ForIdentity(int hash)
        {
            var i = (hash & 0x7fffffff) % (Pairs.Length + 1);
            return i == Pairs.Length ? Slate : Pairs[i].Pair;
        }

        private static Pair Derive(Color c)
        {
            var bg = Mix(c, Color.Parse("#1d1e21"), 0.2);
            var ink = Mix(c, Colors.White, 0.35);
            return new Pair(bg, ink);
        }

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)(a.R * t + b.R * (1 - t)),
            (byte)(a.G * t + b.G * (1 - t)),
            (byte)(a.B * t + b.B * (1 - t)));
    }
}
