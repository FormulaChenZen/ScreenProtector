using System.Globalization;
using System.Windows.Controls;
using ScreenProtector.Localization;

namespace ScreenProtector.Localization
{
    public static class LanguageMenuHelper
    {
        public static void AddLanguageMenu(System.Windows.Controls.ComboBox combo)
        {
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Properties.Resources.Language_Chinese, Tag = "zh-CN" });
            combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = Properties.Resources.Language_English, Tag = "en-US" });

            var current = System.Globalization.CultureInfo.CurrentUICulture.Name;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (((System.Windows.Controls.ComboBoxItem)combo.Items[i]).Tag?.ToString() == current)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }
    }
}
