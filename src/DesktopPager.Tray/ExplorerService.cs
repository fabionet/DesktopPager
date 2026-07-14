using System.Diagnostics;

namespace DesktopPager.Tray;

public static class ExplorerService
{
    /// <summary>
    /// Riavvia Windows Explorer (shell). Chiude tutte le istanze e, se la
    /// shell non riparte da sola, la rilancia. La tray icon dell'app viene
    /// ricreata automaticamente da WinForms alla ricomparsa della taskbar.
    /// </summary>
    public static bool Restart()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                    // processo gia' terminato o non accessibile
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Winlogon di solito rilancia la shell da solo: dagli un momento.
            Thread.Sleep(1500);

            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
