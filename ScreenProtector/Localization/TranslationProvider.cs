using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Reflection;

namespace ScreenProtector.Localization
{
    public class TranslationProvider : INotifyPropertyChanged
    {
        private static readonly TranslationProvider _instance = new TranslationProvider();
        public static TranslationProvider Instance => _instance;

        private readonly ResourceManager _resourceManager;

        private TranslationProvider()
        {
            _resourceManager = new ResourceManager("ScreenProtector.Properties.Resources", Assembly.GetExecutingAssembly());
            Localizer.LanguageChanged += (s, e) => OnLanguageChanged();
        }

        public string this[string key]
        {
            get
            {
                try
                {
                    return _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
                }
                catch
                {
                    return key;
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnLanguageChanged()
        {
            // Notify that indexer values have changed
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }
}
