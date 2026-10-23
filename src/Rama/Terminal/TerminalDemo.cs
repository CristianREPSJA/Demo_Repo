using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace SourceGit.Rama.Terminal
{
    // Entrada oculta de desarrollo: RAMA_TERMINAL_DEMO=1 abre solo el panel de terminal para el directorio actual.
    // RAMA_TERMINAL_DEMO_TAB=shell selecciona la shell; RAMA_TERMINAL_DEMO_INPUT escribe y envía ese texto en la shell
    // (nunca en claude).
    public static class TerminalDemo
    {
        private const int StartTimeoutMs = 15000;

        public static bool TryLaunch(IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Environment.GetEnvironmentVariable("RAMA_TERMINAL_DEMO") != "1")
                return false;

            var panel = new TerminalPanel(Environment.CurrentDirectory);
            if (string.Equals(Environment.GetEnvironmentVariable("RAMA_TERMINAL_DEMO_TAB"), "shell", StringComparison.OrdinalIgnoreCase))
                panel.SelectedTab = panel.ShellTab;

            var window = new Window
            {
                Title = "Rama - Terminal (demo)",
                Width = 1100,
                Height = 560,
                Background = new SolidColorBrush(Color.Parse("#111214")),
                Content = new TerminalPanelView { DataContext = panel },
            };
            window.Opened += async (_, _) => await TypeDemoInputAsync(panel);
            window.Closed += (_, _) => panel.Dispose();
            desktop.MainWindow = window;
            return true;
        }

        private static async Task TypeDemoInputAsync(TerminalPanel panel)
        {
            var input = Environment.GetEnvironmentVariable("RAMA_TERMINAL_DEMO_INPUT");
            if (string.IsNullOrEmpty(input) || panel.SelectedTab != panel.ShellTab)
                return;

            var sw = Stopwatch.StartNew();
            while (panel.ShellTab.Session is not { IsRunning: true } && sw.ElapsedMilliseconds < StartTimeoutMs)
                await Task.Delay(200);
            if (panel.ShellTab.Session is not { IsRunning: true } session)
                return;

            await Task.Delay(1500); // deja que la shell pinte su prompt
            await session.WriteInputAsync(input, submit: true);
        }
    }
}
