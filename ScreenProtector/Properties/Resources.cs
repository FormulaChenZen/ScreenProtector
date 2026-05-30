using System.Globalization;
using System.Resources;

namespace ScreenProtector.Properties
{
    public static class Resources
    {
        private static readonly ResourceManager _resourceManager = new ResourceManager("ScreenProtector.Properties.Resources", typeof(Resources).Assembly);

        public static ResourceManager ResourceManager => _resourceManager;

        public static string Title_App => _resourceManager.GetString("Title_App", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Subtitle_App => _resourceManager.GetString("Subtitle_App", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Startup_Enable => _resourceManager.GetString("Startup_Enable", CultureInfo.CurrentUICulture) ?? string.Empty;

        public static string Brightness_SectionHeader => _resourceManager.GetString("Brightness_SectionHeader", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Brightness_Refresh => _resourceManager.GetString("Brightness_Refresh", CultureInfo.CurrentUICulture) ?? string.Empty;

        public static string Idle_SectionHeader => _resourceManager.GetString("Idle_SectionHeader", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Idle_Enable => _resourceManager.GetString("Idle_Enable", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Idle_SecondsLabel => _resourceManager.GetStringSafe("Idle_SecondsLabel");
        public static string Idle_Description => _resourceManager.GetStringSafe("Idle_Description");

        public static string About_Header => _resourceManager.GetString("About_Header", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string About_Description => _resourceManager.GetString("About_Description", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string About_Feature1 => _resourceManager.GetString("About_Feature1", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string About_Feature2 => _resourceManager.GetString("About_Feature2", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string About_Feature3 => _resourceManager.GetString("About_Feature3", CultureInfo.CurrentUICulture) ?? string.Empty;

        public static string Footer => _resourceManager.GetString("Footer", CultureInfo.CurrentUICulture) ?? string.Empty;

        // Tray and language labels
        public static string Tray_Show => _resourceManager.GetString("Tray_Show", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Tray_Exit => _resourceManager.GetString("Tray_Exit", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Tray_NotifyText => _resourceManager.GetString("Tray_NotifyText", CultureInfo.CurrentUICulture) ?? string.Empty;

        public static string Language_Chinese => _resourceManager.GetString("Language_Chinese", CultureInfo.CurrentUICulture) ?? string.Empty;
        public static string Language_English => _resourceManager.GetStringSafe("Language_English");

        // Additional UI tokens
        public static string Icon_Gear => _resourceManager.GetStringSafe("Icon_Gear");
        public static string PercentSign => _resourceManager.GetStringSafe("PercentSign");
        public static string SecondUnit => _resourceManager.GetStringSafe("SecondUnit");
        public static string Brightness_Placeholder => _resourceManager.GetStringSafe("Brightness_Placeholder");

        // Error messages
        public static string Error_Title => _resourceManager.GetStringSafe("Error_Title");
        public static string Error_CannotSetBrightness => _resourceManager.GetStringSafe("Error_CannotSetBrightness");
        public static string Error_CannotReadBrightness => _resourceManager.GetStringSafe("Error_CannotReadBrightness");
    }

    internal static class ResourceManagerExtensions
    {
        public static string GetStringSafe(this ResourceManager rm, string name)
        {
            try
            {
                return rm.GetString(name, CultureInfo.CurrentUICulture) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
