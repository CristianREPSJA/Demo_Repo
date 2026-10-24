using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;



[assembly: AvaloniaTestApplication(typeof(Rama.VisualTests.TestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Rama.VisualTests;

// App real de Rama (estilos, tema grafito, locale es_CL) sobre la plataforma headless con render Skia.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        VisualEnvironment.Prepare();
        return AppBuilder.Configure<SourceGit.App>()
            .WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" })
            .ConfigureFonts(manager => manager.AddFontCollection(new Avalonia.Media.Fonts.EmbeddedFontCollection(
                new Uri("fonts:SourceGit", UriKind.Absolute),
                new Uri("avares://SourceGit/Resources/Fonts", UriKind.Absolute))))
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .AfterSetup(_ => VisualEnvironment.AfterSetup());
    }
}
