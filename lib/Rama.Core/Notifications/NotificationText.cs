using System.Text;

namespace Rama.Core.Notifications;

internal static class NotificationText
{
    private const int MaxTitle = 120;
    private const int MaxBody = 400;

    // Quita caracteres de control y limita el largo; un guion inicial se desactiva para que no parezca opción.
    public static string Clean(string? text, int max)
    {
        var sb = new StringBuilder();
        foreach (var c in text ?? "")
        {
            if (c is '\r' or '\n' or '\t') sb.Append(' ');
            else if (!char.IsControl(c)) sb.Append(c);
            if (sb.Length >= max) break;
        }
        var s = sb.ToString();
        return s.StartsWith('-') ? " " + s : s;
    }

    public static string Title(string? t) => Clean(t, MaxTitle);
    public static string Body(string? b) => Clean(b, MaxBody);

    public static string XmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
