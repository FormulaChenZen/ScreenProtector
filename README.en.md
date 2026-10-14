[English](https://github.com/FormulaChenZen/ScreenProtector/blob/master/README.en.md) | [中文](https://github.com/FormulaChenZen/ScreenProtector/blob/master/README.md)

# Screen Protector

Screen Protector is a lightweight Windows desktop application designed to protect your eyes by automatically adjusting screen brightness. When it detects that you haven't operated your computer for a long time, the application automatically reduces screen brightness to the minimum, reducing blue light radiation and eye strain.

**✨ Highly Recommended for Laptop Users!** This application not only protects your eyes, but also effectively **prevents OLED screen burn-in** and **extends laptop battery life**. The author has thoroughly tested it on their own laptop and confirms it is **stable, reliable, and worth using**.


## Key Features

- **Auto Brightness Adjustment**: Automatically reduces screen brightness based on the idle time threshold set by the user, protecting your eyes.
- **Automatic Black Screen**: Enable "Black screen when idle" to show black overlays after the same idle threshold, or leave it unchecked to keep automatic dimming. The preference is saved.
- **OLED Screen Burn-in Prevention**: Effectively prevents OLED screen burn-in through automatic brightness reduction and basic screen protection.
- **Extended Laptop Battery Life**: Reducing screen brightness significantly lowers screen power consumption, extending laptop battery life.
- **Manual Brightness Adjustment**: Provides a slider and input box, allowing users to manually adjust screen brightness.
- **Manual Black Screen Protection**: Use the window button or tray menu to cover all displays with black overlays. Mouse or keyboard input closes them.
- **RDP Display Protection**: With automatic switching enabled, connecting over Remote Desktop closes overlays, saves the laptop's hardware brightness, and dims it to minimum. Returning to the local console or exiting restores the original brightness. Local idle preferences are preserved and the countdown restarts on return.
- **Manual/Automatic Mode Selection**: The black screen section offers Local mode (overlays) and Remote mode (dimming). Automatic RDP switching is enabled by default; uncheck it to choose a mode manually. Both preferences are saved.
- **Auto-start on Boot**: Supports setting the application to automatically start on system boot, ensuring features are always active.
- **System Tray Integration**: Minimizes to the system tray without taking up taskbar space, allowing quick access.
- **Customizable Settings**: Allows users to customize idle time thresholds and brightness levels.

## Usage

1. **Launch the Application**: Run `ScreenProtector.exe` to start the application.
2. **Set Idle Duration**: In the application interface, adjust the "Idle Duration" slider to set the idle time threshold for automatic brightness reduction (in seconds).
3. **Enable Automatic Protection**: Turn on "Enable Idle Detection" and optionally check "Black screen when idle". At the configured duration, black overlays appear when checked or the display dims when unchecked. Each continuous idle period triggers once; activity restarts the timer. Set 0 seconds to disable automatic protection.
4. **Manual Brightness Adjustment**: Use the "Brightness" slider or input box to manually adjust screen brightness.
5. **Black Screen**: Click "Black Screen" in the window or tray menu. Move the mouse, click, or press any key to close the overlays.
   **Choose a Mode**: Uncheck "Switch modes automatically with RDP" to use the mode dropdown. Remote mode immediately dims the physical display; switching back to local mode restores brightness and restarts the idle countdown. Local overlays only appear in an active local console session. Automatic switching makes the dropdown display the mode selected by the RDP connection state.
6. **Auto-start on Boot**: Check the "Start with Windows" checkbox to set the application to launch with the system.
7. **Minimize to Tray**: Click the minimize button and the application will hide to the system tray. Double-click the tray icon to restore the window.

## Command-line Arguments

- `-startup`: Launches the application in silent mode, directly hiding to the system tray.

## Important Notes

- The application requires administrator privileges to adjust screen brightness.
- Some displays may not support WMI brightness adjustment, in which case the application will not be able to adjust brightness.
- Black overlays cover the displays while keeping them powered on. Display and system wake requests are held while the overlays are visible and released on exit.
- Black screen protection does not modify power schemes or Windows sign-in settings. Independent security policies and manual locking can still apply; the app avoids idle display power off triggering a sign-in prompt.
- No overlays are shown in RDP sessions or sessions detached from the local console. Black screen controls pause while preserving your preferences. Remote mode uses WMI to dim the laptop's physical display without covering the remote desktop.
- Minimum brightness does not power off the display. Existing Windows power policies still control display power; the app does not change display timeout, sleep, or sign-in settings. External displays and hardware without WMI brightness support cannot use this dimming method; failures show a message.
- With automatic switching, returning locally or exiting normally restores brightness; RDP disconnection keeps it dim until local return. In manual mode, switching to local mode or exiting normally restores brightness. Failed restoration preserves the original value and retries; if exit restoration fails, the app stays running and reports the error.
- If you need to uninstall, please close the application first and remove the startup entry.

## Localization
This project uses .resx resource files for localization. English resources are provided in Properties/Resources.en-US.resx and default (Chinese) resources in Properties/Resources.resx. The UI will pick the resource set based on the system's UI culture.

## Building
Requires .NET 8 and Windows. Open the solution in Visual Studio 2022/2024/2026 and build the project.

Run `dotnet run --project tests/ScreenProtector.PowerTests` to check overlay lifecycle, wake request cleanup, idle triggering, RDP session detection, and brightness save/restore with fake APIs. These checks do not show overlays, change hardware brightness, or modify power settings.

## Discover More Tools

If you need more useful tools and features, visit our online tool website: **[🌐 Rumystic.com](https://rumystic.com)**

We provide a variety of web tools, productivity applications, and developer utilities to help you boost your efficiency. If you have any suggestions or feature requests for Screen Protector, feel free to contact us through our website.

## License
MIT
