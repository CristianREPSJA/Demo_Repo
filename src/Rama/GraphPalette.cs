using System.Collections.Generic;

using Avalonia.Media;

namespace SourceGit.Branding
{
    // Carriles del grafo de Rama: sky, mint, amber, rose, lilac y luego colores de apoyo.
    public static class GraphPalette
    {
        public static void Apply(double thickness = 2)
        {
            Models.CommitGraph.SetPens(Lanes, thickness);
        }

        public static readonly List<Color> Lanes = [
            Color.Parse("#5aa9e6"),
            Color.Parse("#5cc58a"),
            Color.Parse("#e6a23c"),
            Color.Parse("#e0718f"),
            Color.Parse("#b294e6"),
            Colors.Turquoise,
            Colors.Khaki,
            Colors.Olive,
            Colors.RoyalBlue,
            Colors.Teal,
        ];
    }
}
