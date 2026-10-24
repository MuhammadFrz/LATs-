An initial Windows 11 desktop client, implemented in C# / .NET 10 with WPF and the native Windows WLAN API.

### Download

Download **LinkDesk-windows-x64.zip**, extract it, and run **LinkDesk.exe**. The package includes the .NET runtime. The executable is unsigned. Source code and SHA-256 checksums for both ZIP downloads are attached separately.

### Included

- Discover nearby phone hotspots and display signal strength.
- Windows Location permission guidance and a Settings shortcut.
- WPA2-Personal/AES association using a temporary Wi-Fi profile.
- A 30-second connection deadline, cancellation, and confirmation of the selected SSID.

Connecting switches the selected adapter away from its current Wi-Fi network. The previous connection is not automatically restored on cancellation/failure.

### Preview limitations

**PdaNet internet forwarding is not implemented. This release is not a complete PdaNet replacement and does not claim to fix the reported Wi-Fi Direct failure.** USB/Bluetooth tethering, WPS, and driver integration are not included. No PdaNet binaries have been reverse engineered.

Release automation runs the 13 profile/SSID/native ABI checks and builds the Windows executable before publication. These checks do not validate the GUI or radio behavior on a physical Windows machine. Compatibility with the Galaxy A54 / Intel AX201 and end-to-end phone internet access remain unverified.
