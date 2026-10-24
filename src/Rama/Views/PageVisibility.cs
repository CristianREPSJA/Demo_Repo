using System;

using Avalonia;
using Avalonia.Controls;

namespace SourceGit.Views
{
    // Las paginas del riel van dentro de un Panel que lleva IsVisible (ver Launcher.axaml): la visibilidad
    // efectiva de la pagina es la propia y la del Panel contenedor.
    public static class PageVisibility
    {
        public static bool IsShown(Control page) =>
            page.IsVisible && (page.Parent is not Visual parent || parent.IsVisible);

        // Llama onChange(visible) al cambiar la visibilidad de la pagina o de su contenedor directo.
        public static IDisposable Watch(Control page, Action<bool> onChange)
        {
            var observer = new Avalonia.Reactive.AnonymousObserver<bool>(_ => onChange(IsShown(page)));
            var own = page.GetObservable(Visual.IsVisibleProperty).Subscribe(observer);
            var parent = (page.Parent as Visual)?.GetObservable(Visual.IsVisibleProperty).Subscribe(observer);
            return new Subscriptions(own, parent);
        }

        private sealed class Subscriptions(params IDisposable[] items) : IDisposable
        {
            public void Dispose()
            {
                foreach (var i in items)
                    i?.Dispose();
            }
        }
    }
}
