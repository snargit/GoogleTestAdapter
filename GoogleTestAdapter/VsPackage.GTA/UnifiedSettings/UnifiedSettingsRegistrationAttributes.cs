using System;
using System.Globalization;
using Microsoft.VisualStudio.Shell;

namespace GoogleTestAdapter.VsPackage.UnifiedSettings
{

    // Registers the package's Unified Settings manifest, which makes Visual Studio show the options in its new settings
    // UI (VS 2026, and VS 2022 17.10+ if its preview feature is enabled). Writes what Microsoft's
    // ProvideSettingsManifestAttribute writes, which the VS 2017 SDK referenced by GTA does not provide; older versions
    // of Visual Studio ignore the key.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ProvideUnifiedSettingsManifestAttribute : RegistrationAttribute
    {
        private const string KeyName = "SettingsManifests";

        public string PackageRelativeManifestFile { get; }

        public ProvideUnifiedSettingsManifestAttribute(string packageRelativeManifestFile)
        {
            PackageRelativeManifestFile = packageRelativeManifestFile;
        }

        public override void Register(RegistrationContext context)
        {
            using (Key key = context.CreateKey(GetKeyPath(context)))
            {
                key.SetValue(string.Empty, context.ComponentType.FullName);
                key.SetValue("ManifestPath", $@"{context.ComponentPath}\{PackageRelativeManifestFile}");
                // Visual Studio caches manifests, and reads them again when the cache tag changes: the time of the build
                key.SetValue("CacheTag", DateTimeOffset.UtcNow.Ticks);
            }
        }

        public override void Unregister(RegistrationContext context)
        {
            context.RemoveKey(GetKeyPath(context));
        }

        private static string GetKeyPath(RegistrationContext context)
        {
            return $@"{KeyName}\{context.ComponentType.GUID.ToString("B").ToUpper(CultureInfo.InvariantCulture)}";
        }
    }

    // Marks an options page (registered with ProvideOptionPageAttribute) as migrated to Unified Settings: Visual Studio
    // hides it while its new settings UI is active, and shows it otherwise (e.g. in VS 2017 to 2022)
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class ProvideOptionPageInUnifiedSettingsAttribute : RegistrationAttribute
    {
        public string CategoryName { get; }
        public string PageName { get; }

        public ProvideOptionPageInUnifiedSettingsAttribute(string categoryName, string pageName)
        {
            CategoryName = categoryName;
            PageName = pageName;
        }

        public override void Register(RegistrationContext context)
        {
            using (Key key = context.CreateKey($@"ToolsOptionsPages\{CategoryName}\{PageName}"))
            {
                key.SetValue("IsInUnifiedSettings", 1);
            }
        }

        public override void Unregister(RegistrationContext context)
        {
        }
    }

}
