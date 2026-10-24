# LinkDesk

A new C# / .NET 10 WPF Windows client for discovering and associating with phone hotspots. Designed around Windows 11 and the reported Galaxy A54 / PdaNet 5.32 workflow.

**This is an early Wi-Fi connection client, not a complete PdaNet replacement.** It does not implement PdaNet's proprietary internet forwarding, USB/Bluetooth tethering, WPS, drivers, or activation. No PdaNet binaries have been supplied or reverse engineered. The UI is an original implementation informed by the supplied screenshots.

## What works in the implementation

- Fresh native WLAN scans, per-adapter network identities, signal strength, and optional display of other networks.
- Windows Location access errors with a shortcut to the right Settings page.
- Temporary WPA2-Personal/AES profiles using the exact SSID bytes; credentials are not written to a saved profile by LinkDesk or logged. Windows handles the credential during connection, and managed strings cannot guarantee memory zeroization.
- Thirty-second connection deadline, cancellation, and confirmation of the selected SSID through Windows connection state.
- Clear separation between Wi-Fi association and internet forwarding. No simulated devices, traffic statistics, or success states.

Connecting changes the selected adapter's current Wi-Fi connection. Cancellation/failure stops that pending request; the previous connection is not automatically restored. Closing after successful association leaves the Windows Wi-Fi connection active. No system proxy settings or drivers are changed.

## Build and run on Windows 11

Install the .NET 10 SDK selected by `global.json` (10.0.402 or a newer patch in that feature band), then from the repository root:

```powershell
dotnet run --project src/LinkDesk/LinkDesk.csproj
```

Enable Wi-Fi and Windows **Settings → Privacy & security → Location → Location services** and app access. Start the phone's PdaNet **Wi-Fi Direct Hotspot**, select its network in LinkDesk, enter the password shown on the phone, and connect. Association to an advertised SSID is the current implementation route; compatibility with the phone's specific Wi-Fi Direct group has not been established on hardware.

Create a standalone Windows x64 build (no .NET runtime installation needed on the destination):

```powershell
dotnet publish src/LinkDesk/LinkDesk.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/win-x64
```

Run `artifacts/win-x64/LinkDesk.exe`. The executable is unsigned.

## Checks

```powershell
dotnet run --project tests/LinkDesk.Checks/LinkDesk.Checks.csproj -c Release
dotnet build src/LinkDesk/LinkDesk.csproj -c Release
```

The dependency-free checks exercise profile encoding, SSID identity, invalid credentials/security, and native ABI structure layouts. They run on Linux as well as Windows. Cross-compilation checks C#/XAML but cannot validate rendering, Windows permissions, radio behavior, connection cancellation, or phone internet forwarding.

## Investigation status and next milestone

Observed from user-supplied evidence: Windows initially denied network queries due to Location permission; a later PdaNet screenshot found the phone but remained at “Connecting…”. This does not establish whether the remaining failure is authentication, Wi-Fi Direct negotiation, IP configuration, or PdaNet transport startup. “Hosted network supported: No” alone does not diagnose Wi-Fi Direct support.

REA's skill and CLI were inspected separately under `/workspace/rea`. There is no selected PdaNet artifact or REA analysis evidence yet. To implement a compatible forwarding backend, obtain the exact 5.32 desktop installer/files and inspect their protocol with REA. Then test on Windows 11 with the AX201 and Galaxy A54: discovery; correct/incorrect password; permission denied; timeout/cancel; closing during an operation; adapter switching; IP assignment; and an internet request proven to use the phone transport. Existing PC connectivity alone must not count as tethering verification.
