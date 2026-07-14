using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace DesktopPager.Tray;

/// <summary>
/// Indicatore di rete: tipo di connessione (WiFi/Ethernet/offline) e, per il
/// WiFi, la qualità del segnale letta da "netsh wlan show interfaces"
/// (con throttling per non pesare).
/// </summary>
public sealed class NetworkService
{
    public enum Kind { Offline, Wifi, Ethernet, Other }

    private int _cachedSignal = -1;
    private int _tick;

    public (Kind kind, int signal) Get()
    {
        var kind = Kind.Offline;
        var isWifi = false;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                // deve avere un gateway (connessione reale)
                if (ni.GetIPProperties().GatewayAddresses.Count == 0)
                {
                    continue;
                }

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    kind = Kind.Wifi;
                    isWifi = true;
                    break;
                }

                if (kind == Kind.Offline)
                {
                    kind = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? Kind.Ethernet : Kind.Other;
                }
            }
        }
        catch
        {
            // in caso di errore: offline
        }

        // segnale WiFi da netsh, aggiornato ogni ~5 chiamate
        if (isWifi)
        {
            if (_tick <= 0)
            {
                _cachedSignal = ReadWifiSignal();
                _tick = 5;
            }
            _tick--;
        }
        else
        {
            _cachedSignal = -1;
        }

        return (kind, isWifi ? _cachedSignal : -1);
    }

    private static int ReadWifiSignal()
    {
        try
        {
            var netsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
            var psi = new ProcessStartInfo
            {
                FileName = netsh,
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                return -1;
            }

            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(1500);

            // la riga del segnale contiene "gnal" (Signal / Segnale) e "NN%"
            foreach (var line in output.Split('\n'))
            {
                if (line.IndexOf("gnal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var m = Regex.Match(line, @"(\d+)\s*%");
                    if (m.Success)
                    {
                        return int.Parse(m.Groups[1].Value);
                    }
                }
            }
        }
        catch
        {
            // netsh non disponibile
        }

        return -1;
    }
}
