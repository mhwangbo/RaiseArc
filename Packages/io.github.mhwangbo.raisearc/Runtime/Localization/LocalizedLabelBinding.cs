using System;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

namespace PrincessStudio.Localization
{
    /// <summary>Event-driven UI Toolkit binding; dispose with the owning view.</summary>
    public sealed class LocalizedLabelBinding : IDisposable
    {
        private readonly LocalizedString reference;
        private readonly Label label;
        public LocalizedLabelBinding(Label label, string table, string key, params object[] arguments)
        {
            this.label = label ?? throw new ArgumentNullException(nameof(label));
            reference = new LocalizedString(table, key);
            if (arguments.Length > 0)
                reference.Arguments = arguments;
            reference.StringChanged += Update;
        }
        private void Update(string value)
        {
            label.text = value;
        }
        public void Dispose()
        {
            reference.StringChanged -= Update;
        }
        public static void SelectLocale(string code)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code) ?? throw new ArgumentException("Locale is not configured: " + code);
            LocalizationSettings.SelectedLocale = locale;
        }
    }
}
