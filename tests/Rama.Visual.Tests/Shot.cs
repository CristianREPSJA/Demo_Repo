using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Rama.VisualTests;

// Utilidades de render: asienta el dispatcher, captura el frame renderizado (Skia, en proceso) y guarda el PNG.
public static class Shot
{
    public static async Task Settle(int rounds = 6, int delayMs = 40)
    {
        for (var i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(delayMs);
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    public static async Task<string> Save(Window window, string name, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        if (!window.IsVisible)
            window.Show();
        Screens.Track(window);

        window.Width = width;
        window.Height = height;
        await Settle();

        var file = Path.Combine(VisualEnvironment.OutDir, name + ".png");
        using (var frame = window.CaptureRenderedFrame())
            frame!.Save(file);

        VisualEnvironment.Sink.Mark($"{name}: {CountUnresolvedTexts(window)} textos con clave sin resolver");
        foreach (var t in UnresolvedTexts(window))
            VisualEnvironment.Sink.Mark($"{name}: clave de locale sin resolver -> {t}");
        return file;
    }

    // App.Text devuelve "Text.Clave" cuando falta; los DynamicResource faltantes quedan vacíos (ver prueba estática).
    public static IEnumerable<string> UnresolvedTexts(Visual root) =>
        root.GetSelfAndVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrEmpty(t) &&
                        (t.StartsWith("Text.", StringComparison.Ordinal) || t.StartsWith("Rama.", StringComparison.Ordinal)))
            .Distinct();

    private static int CountUnresolvedTexts(Visual root) => UnresolvedTexts(root).Count();

    public static T Find<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().First();

    public static void SetField(object target, string name, object value)
    {
        var f = FindField(target.GetType(), name) ?? throw new MissingFieldException(target.GetType().Name, name);
        f.SetValue(target, value);
    }

    public static object Invoke(object target, string name, params object[] args)
    {
        for (var t = target.GetType(); t != null; t = t.BaseType)
        {
            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (m != null)
                return m.Invoke(target, args);
        }

        throw new MissingMethodException(target.GetType().Name, name);
    }

    public static void SetProperty(object target, string name, object value)
    {
        var p = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingMemberException(target.GetType().Name, name);
        p.SetValue(target, value);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (f != null)
                return f;
        }

        return null;
    }
}
