namespace DesktopPager.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Istanza WPF (senza avviarne il loop): fornisce un Application.Current
        // così le finestre WPF con AllowsTransparency (overlay gelatina) si
        // possono mostrare da questo thread ospitato da WinForms.
        _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        Application.Run(new TrayApplicationContext());
    }
}
