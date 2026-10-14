using System.Net.NetworkInformation;
using Microsoft.Maui.Networking;

namespace PKForge.App.Platforms.Desktop;

public sealed class DesktopConnectivity : IConnectivity
{
    public NetworkAccess NetworkAccess =>
        NetworkInterface.GetIsNetworkAvailable() ? NetworkAccess.Internet : NetworkAccess.None;

    public IEnumerable<ConnectionProfile> ConnectionProfiles =>
        [ConnectionProfile.WiFi, ConnectionProfile.Ethernet];

    public event EventHandler<ConnectivityChangedEventArgs>? ConnectivityChanged;

    public DesktopConnectivity()
    {
        try
        {
            NetworkChange.NetworkAvailabilityChanged += (_, _) =>
            {
                ConnectivityChanged?.Invoke(this, new ConnectivityChangedEventArgs(NetworkAccess, ConnectionProfiles));
            };
        }
        catch
        {
            // Best effort
        }
    }
}
