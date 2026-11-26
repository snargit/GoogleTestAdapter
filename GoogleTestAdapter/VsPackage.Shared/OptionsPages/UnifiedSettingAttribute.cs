using System;

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    // The moniker of an option in Visual Studio's Unified Settings (its new settings UI, and settings.json), which keeps
    // the option's value while that UI is active (see UnifiedSettingsBridge). The registration manifest
    // (Packaging.GTA\UnifiedSettings\GoogleTestAdapter.registration.json) is generated from the options pages, this
    // attribute included, by UnifiedSettingsRegistrationTests; monikers are stored in users' settings, so don't change
    // them.
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class UnifiedSettingAttribute : Attribute
    {
        public const int NoLimit = int.MinValue;

        public string Moniker { get; }

        // Constraints of the manifest, which the settings UI enforces; they must match the setter's validation, since
        // the setter is not called while a value is edited there
        public int Minimum { get; set; } = NoLimit;
        public int Maximum { get; set; } = NoLimit;
        public int MaxLength { get; set; } = NoLimit;

        public UnifiedSettingAttribute(string moniker)
        {
            Moniker = moniker;
        }
    }

}
