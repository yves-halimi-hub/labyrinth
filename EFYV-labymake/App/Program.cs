using System;
using Avalonia;

namespace EFYVLabyMake.App
{
    public static class Program
    {
        // DesignerSession is synchronous and not thread-safe: every session call in
        // this app happens on the Avalonia UI thread. The only async seams are the
        // persistence saves, which capture an immutable snapshot on the UI thread
        // first (see EditorShell.SaveAsync).
        [STAThread]
        public static int Main(string[] args)
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<EditorApp>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
