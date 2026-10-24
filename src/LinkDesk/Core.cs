using System.Security;
using System.Text;

namespace LinkDesk;

public sealed record WifiNetwork(Guid AdapterId, string AdapterName, byte[] Ssid, uint Signal,
    uint Authentication, uint Cipher, bool Connectable, uint Reason)
{
    public string Name => Encoding.UTF8.GetString(Ssid);
    public string Identity => $"{AdapterId}:{Convert.ToHexString(Ssid)}";
    public string Detail => $"{Signal}% signal  ·  {AdapterName}";
    public bool IsPhone => Name.StartsWith("DIRECT-", StringComparison.OrdinalIgnoreCase);
    public bool Supported => Authentication == 7 && Cipher == 4; // WPA2-Personal / CCMP
}

public static class WifiProfile
{
    // Use the SSID's exact bytes. Display names can contain XML characters or invalid UTF-8.
    public static string Create(WifiNetwork network, string password)
    {
        if (network.Ssid.Length is < 1 or > 32) throw new ArgumentException("Invalid SSID length.");
        if (!network.Connectable) throw new InvalidOperationException($"Windows cannot connect to this network (reason {network.Reason}).");
        if (!network.Supported) throw new NotSupportedException("This preview supports WPA2-Personal (AES) hotspots. This network advertises a different security mode.");
        bool rawKey = password.Length == 64 && password.All(Uri.IsHexDigit);
        if (!rawKey && (password.Length is < 8 or > 63 || password.Any(c => c < 32 || c > 126)))
            throw new ArgumentException("Enter the phone's password: 8–63 printable ASCII characters, or a 64-digit hexadecimal key.");
        string key = SecurityElement.Escape(password)!;
        return $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
              <name>LinkDesk-{Convert.ToHexString(network.Ssid)}</name>
              <SSIDConfig><SSID><hex>{Convert.ToHexString(network.Ssid)}</hex></SSID></SSIDConfig>
              <connectionType>ESS</connectionType><connectionMode>manual</connectionMode>
              <MSM><security><authEncryption><authentication>WPA2PSK</authentication><encryption>AES</encryption><useOneX>false</useOneX></authEncryption>
              <sharedKey><keyType>{(rawKey ? "networkKey" : "passPhrase")}</keyType><protected>false</protected><keyMaterial>{key}</keyMaterial></sharedKey>
              </security></MSM>
            </WLANProfile>
            """;
    }
}
