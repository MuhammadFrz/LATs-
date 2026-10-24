using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using LinkDesk;

int passed = 0;
void Check(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine($"PASS {name}");
}
void Require(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject<T>(Action test) where T : Exception
{
    try { test(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
WifiNetwork network = new(Guid.NewGuid(), "Test adapter", Encoding.UTF8.GetBytes("DIRECT-test-Phone"), 82, 7, 4, true, 0);
XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";

Check("SSID bytes are preserved and credential XML is escaped", () =>
{
    var special = network with { Ssid = [0x44, 0x00, 0xFF, 0x3C, 0x26] };
    var doc = XDocument.Parse(WifiProfile.Create(special, "pass<&>\"word"));
    Require(doc.Descendants(ns + "hex").Single().Value == "4400FF3C26");
    Require(doc.Descendants(ns + "keyMaterial").Single().Value == "pass<&>\"word");
    Require(doc.Descendants(ns + "keyType").Single().Value == "passPhrase");
});
Check("WPA2 AES manual profile is explicit", () =>
{
    var doc = XDocument.Parse(WifiProfile.Create(network, "abcdefgh"));
    Require(doc.Descendants(ns + "authentication").Single().Value == "WPA2PSK");
    Require(doc.Descendants(ns + "encryption").Single().Value == "AES");
    Require(doc.Descendants(ns + "connectionMode").Single().Value == "manual");
});
Check("64 hex digits use networkKey", () => Require(XDocument.Parse(WifiProfile.Create(network, new string('a', 64))).Descendants(ns + "keyType").Single().Value == "networkKey"));
Check("empty password is rejected without implicit WPS", () => Reject<ArgumentException>(() => WifiProfile.Create(network, "")));
Check("short password is rejected", () => Reject<ArgumentException>(() => WifiProfile.Create(network, "1234567")));
Check("non-hex 64-character password is rejected", () => Reject<ArgumentException>(() => WifiProfile.Create(network, new string('z', 64))));
Check("control characters are rejected", () => Reject<ArgumentException>(() => WifiProfile.Create(network, "abcdefgh\n")));
Check("unsupported security is rejected", () => Reject<NotSupportedException>(() => WifiProfile.Create(network with { Authentication = 9 }, "abcdefgh")));
Check("unconnectable networks are rejected", () => Reject<InvalidOperationException>(() => WifiProfile.Create(network with { Connectable = false }, "abcdefgh")));
Check("empty SSIDs are rejected", () => Reject<ArgumentException>(() => WifiProfile.Create(network with { Ssid = [] }, "abcdefgh")));
Check("oversized SSIDs are rejected", () => Reject<ArgumentException>(() => WifiProfile.Create(network with { Ssid = new byte[33] }, "abcdefgh")));
Check("same SSID on different adapters has distinct identity", () => Require(network.Identity != (network with { AdapterId = Guid.NewGuid() }).Identity));
Check("native WLAN structure layout matches Windows ABI", () =>
{
    Require(Marshal.SizeOf<NativeWifi.InterfaceInfo>() == 532);
    Require(Marshal.SizeOf<NativeWifi.Ssid>() == 36);
    Require(Marshal.SizeOf<NativeWifi.AvailableNetwork>() == 628);
    Require(Marshal.OffsetOf<NativeWifi.AvailableNetwork>(nameof(NativeWifi.AvailableNetwork.Signal)).ToInt32() == 604);
});
Console.WriteLine($"{passed} checks passed. These checks do not execute Windows WLAN or verify tethering.");
