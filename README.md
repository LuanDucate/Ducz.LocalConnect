# Ducz LocalConnect

Ducz LocalConnect is a simple Windows desktop application for remote access between two computers on the same local network.

It provides a lightweight host/client workflow focused on LAN usage, with screen streaming, remote mouse and keyboard input, remote audio playback, PIN-based authentication, clipboard transfer, and one-way file transfer from client to host.

## Features

- Windows desktop app built with .NET 10 and WinForms
- Host/client connection over local network
- PIN authentication before the session starts
- Remote screen streaming with differential frame updates
- Host-side monitor selection for multi-display setups
- Remote mouse and keyboard control
- Remote system audio playback on the client
- Full screen mode with `F11` and `Esc`
- Manual clipboard sync between client and host
- Manual file transfer from client to host
- App icon and branded UI

## Requirements

- Windows 10 or Windows 11
- .NET 10 SDK for development
- No external server required
- Both computers must be on the same LAN

## Quick Start

### Host

1. Open the `Host` tab.
2. Confirm the port. The default is `5050`.
3. Choose which monitor to share when the host has multiple displays.
4. Set the session PIN.
5. Click `Start host`.
6. Share the displayed local IP address with the client.
7. Audio is published automatically on the next port. Example: video on `5050`, audio on `5051`.

### Client

1. Open the `Client` tab.
2. Enter the host IP and the same base port.
3. Enter the same PIN configured on the host.
4. Click `Connect`.
5. Click the remote image to capture mouse and keyboard input.
6. Use `F11` to enter full screen and `Esc` to exit.
7. Use `Send clipboard`, `Fetch remote clipboard`, and `Send file` when needed.

## Performance Notes

The app now uses differential frame updates instead of sending a full screen image every cycle. That reduces bandwidth and improves responsiveness, especially for cursor movement and text entry.

If you still see delay on the client:

- Prefer wired Ethernet over Wi-Fi
- Use the application on a fast local network only
- Avoid very high-resolution displays when possible
- Close GPU-heavy or video-heavy applications on the host
- Keep both machines on the same switch or access point

## Security Notes

This project is currently intended for trusted local networks.

Current limitations:

- No transport encryption yet
- No device discovery yet
- File transfer is client-to-host only

## License

See [LICENSE.txt](LICENSE.txt).
