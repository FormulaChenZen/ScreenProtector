# Screen Protector

Screen Protector is a lightweight Windows desktop application designed to protect your eyes by automatically adjusting screen brightness. When it detects that you haven't operated your computer for a long time, the application automatically reduces screen brightness to the minimum, reducing blue light radiation and eye strain.

**✨ Highly Recommended for Laptop Users!** This application not only protects your eyes, but also effectively **prevents OLED screen burn-in** and **extends laptop battery life**. The author has thoroughly tested it on their own laptop and confirms it is **stable, reliable, and worth using**.

For Chinese documentation, please see README.md.

## Key Features

- **Auto Brightness Adjustment**: Automatically reduces screen brightness based on the idle time threshold set by the user, protecting your eyes.
- **OLED Screen Burn-in Prevention**: Effectively prevents OLED screen burn-in through automatic brightness reduction and basic screen protection.
- **Extended Laptop Battery Life**: Reducing screen brightness significantly lowers screen power consumption, extending laptop battery life.
- **Manual Brightness Adjustment**: Provides a slider and input box, allowing users to manually adjust screen brightness.
- **Auto-start on Boot**: Supports setting the application to automatically start on system boot, ensuring features are always active.
- **System Tray Integration**: Minimizes to the system tray without taking up taskbar space, allowing quick access.
- **Customizable Settings**: Allows users to customize idle time thresholds and brightness levels.

## Usage

1. **Launch the Application**: Run `ScreenProtector.exe` to start the application.
2. **Set Idle Duration**: In the application interface, adjust the "Idle Duration" slider to set the idle time threshold for automatic brightness reduction (in seconds).
3. **Enable Auto Adjustment**: Check the "Enable Idle Detection" checkbox to activate the automatic brightness adjustment feature.
4. **Manual Brightness Adjustment**: Use the "Brightness" slider or input box to manually adjust screen brightness.
5. **Auto-start on Boot**: Check the "Start with Windows" checkbox to set the application to launch with the system.
6. **Minimize to Tray**: Click the minimize button and the application will hide to the system tray. Double-click the tray icon to restore the window.

## Command-line Arguments

- `-startup`: Launches the application in silent mode, directly hiding to the system tray.

## Important Notes

- The application requires administrator privileges to adjust screen brightness.
- Some displays may not support WMI brightness adjustment, in which case the application will not be able to adjust brightness.
- If you need to uninstall, please close the application first and remove the startup entry.

## Localization
This project uses .resx resource files for localization. English resources are provided in Properties/Resources.en-US.resx and default (Chinese) resources in Properties/Resources.resx. The UI will pick the resource set based on the system's UI culture.

## Building
Requires .NET 8 and Windows. Open the solution in Visual Studio 2022/2024/2026 and build the project.

## Discover More Tools

If you need more useful tools and features, visit our online tool website: **[🌐 Rumystic.com](https://rumystic.com)**

We provide a variety of web tools, productivity applications, and developer utilities to help you boost your efficiency. If you have any suggestions or feature requests for Screen Protector, feel free to contact us through our website.

## License
MIT
