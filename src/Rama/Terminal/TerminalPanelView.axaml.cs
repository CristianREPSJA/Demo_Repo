using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Iciclecreek.Terminal;
using Rama.Terminal;

namespace SourceGit.Rama.Terminal
{
    // Aloja un TerminalControl por pestaña. El PTY de cada pestaña se lanza al mostrarla por primera vez.
    public partial class TerminalPanelView : UserControl
    {
        private static readonly IBrush TerminalBackground = new SolidColorBrush(Color.Parse("#111214"));
        private static readonly IBrush TerminalForeground = new SolidColorBrush(Color.Parse("#D8D9DC"));
        private static readonly IBrush ErrorForeground = new SolidColorBrush(Color.Parse("#A9ABB1"));

        public TerminalPanelView()
        {
            InitializeComponent();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            TerminalHost.Children.Clear();
            if (DataContext is not TerminalPanel panel)
                return;

            foreach (var tab in panel.Tabs)
                TerminalHost.Children.Add(CreateHost(panel, tab));
        }

        private Control CreateHost(TerminalPanel panel, TerminalTab tab)
        {
            if (tab.HasError)
                return Bind(MessageBlock(tab.Error), tab);

            var control = new TerminalControl
            {
                FontFamily = this.FindResource("Fonts.Monospace") as FontFamily ?? FontFamily.Default,
                FontSize = 12.5,
                BufferSize = 5000,
                Background = TerminalBackground,
                Foreground = TerminalForeground,
                Padding = new Thickness(16, 12),
            };
            control.Loaded += (_, _) => TryStart(panel, tab, control);
            control.SizeChanged += (_, _) => Dispatcher.UIThread.Post(panel.RefreshStatus, DispatcherPriority.Background);
            tab.PropertyChanged += (_, args) => OnTabPropertyChanged(panel, tab, control, args);
            return Bind(control, tab);
        }

        private static Control Bind(Control control, TerminalTab tab)
        {
            control.IsVisible = tab.IsSelected;
            tab.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TerminalTab.IsSelected))
                    control.IsVisible = tab.IsSelected;
            };
            return control;
        }

        private void OnTabPropertyChanged(TerminalPanel panel, TerminalTab tab, TerminalControl control, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(TerminalTab.IsSelected) || !tab.IsSelected)
                return;
            Dispatcher.UIThread.Post(() =>
            {
                TryStart(panel, tab, control);
                control.Focus();
            }, DispatcherPriority.Loaded);
        }

        private async void TryStart(TerminalPanel panel, TerminalTab tab, TerminalControl control)
        {
            if (!tab.IsSelected || !control.IsLoaded || tab.Session != null)
                return;
            try
            {
                await panel.AttachAsync(tab, new TerminalSession(control));
                control.Focus();
            }
            catch (Exception ex)
            {
                var index = TerminalHost.Children.IndexOf(control);
                if (index >= 0)
                    TerminalHost.Children[index] = Bind(MessageBlock($"No se pudo abrir la terminal: {ex.Message}"), tab);
            }
        }

        private static TextBlock MessageBlock(string text) => new()
        {
            Text = text,
            Foreground = ErrorForeground,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
        };

        private void OnTabClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TerminalTab tab } && DataContext is TerminalPanel panel)
                panel.SelectedTab = tab;
            e.Handled = true;
        }

        private async void OnExplainClick(object sender, RoutedEventArgs e) => await RunPrompt(p => p.ExplainChangesAsync(), e);
        private async void OnCommitClick(object sender, RoutedEventArgs e) => await RunPrompt(p => p.CommitMessageAsync(), e);
        private async void OnReviewClick(object sender, RoutedEventArgs e) => await RunPrompt(p => p.ReviewBranchAsync(), e);
        private async void OnSummarizeClick(object sender, RoutedEventArgs e) => await RunPrompt(p => p.SummarizeTicketAsync(), e);

        private async Task RunPrompt(Func<TerminalPanel, Task> action, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is not TerminalPanel panel)
                return;
            try
            {
                await action(panel);
            }
            catch (Exception ex)
            {
                Native.OS.LogException(ex);
            }
        }
    }
}
