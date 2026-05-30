using System;
using System.Globalization;

namespace ScreenProtector.Localization
{
    public static class Localizer
    {
        public static event EventHandler? LanguageChanged;

        public static CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;

        public static void SetCulture(CultureInfo culture)
        {
            if (culture == null) return;
            if (Equals(CultureInfo.CurrentUICulture, culture)) return;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        public static string Translate(string key)
        {
            try
            {
                // Use ResourceManager via Reflection since Resources.ResourceManager is not public
                var resManagerProp = typeof(ScreenProtector.Properties.Resources).GetProperty("ResourceManager", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (resManagerProp != null)
                {
                    var rm = resManagerProp.GetValue(null) as System.Resources.ResourceManager;
                    if (rm != null)
                    {
                        return rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
                    }
                }
                return key;
            }
            catch
            {
                return key;
            }
        }
    }
}
