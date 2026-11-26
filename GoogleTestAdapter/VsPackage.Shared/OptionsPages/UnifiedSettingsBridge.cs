using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using GoogleTestAdapter.TestAdapter.Framework;
using Microsoft.VisualStudio.Shell;

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    // Connects the options pages with Visual Studio's Unified Settings (its new settings UI, and settings.json).
    // DialogPage (VS 2022 17.14 and later) reads an option from Unified Settings instead of its own store, writes it
    // there, and follows its changes, if the option's property descriptor carries Visual Studio's
    // UnifiedSettingsMonikerAttribute, while Unified Settings are active (VS 2026, and VS 2022 17.14). GTA references
    // the VS 2017 SDK, which lacks the attribute, so it is found at runtime: public in VS 2026, internal in VS 2022.
    // Older versions of Visual Studio don't have it, and get the pages' descriptors unchanged.
    //
    // While Visual Studio shows its new settings UI instead of the options pages, the descriptors ignore invalid values
    // (reporting them to InvalidValueHandler) instead of failing: the settings UI can't validate e.g. regexes as the
    // pages' setters do, and DialogPage would fail to load the page. Otherwise, the pages' property grid uses the
    // descriptors, and shows the setters' errors.
    public static class UnifiedSettingsBridge
    {
        private const string PublicMonikerAttribute = "Microsoft.VisualStudio.Shell.UnifiedSettingsMonikerAttribute";
        private const string InternalMonikerAttribute = "Microsoft.Internal.VisualStudio.Services.UnifiedSettings.UnifiedSettingsMonikerAttribute";
        private const string InternalMonikerAttributeAssembly = "Microsoft.Internal.VisualStudio.Shell.Framework";

        private static readonly Lazy<Type> MonikerAttributeType = new Lazy<Type>(FindMonikerAttributeType);
        private static readonly Lazy<UIContext> UnifiedSettingsActiveContext = new Lazy<UIContext>(FindUnifiedSettingsActiveContext);

        // Receives the option's descriptor, the ignored value, and the error message
        public static Action<PropertyDescriptor, object, string> InvalidValueHandler { get; set; }

        public static bool IsSupported => MonikerAttributeType.Value != null;

        // Unified Settings keep the options' values; VS 2022 17.14 activates them even if its preview feature (the new
        // settings UI) is off, showing the options pages
        public static bool IsActive => UnifiedSettingsActiveContext.Value?.IsActive ?? false;

        // VS shows its new settings UI instead of the options pages: always in VS 2026, with the preview feature
        // "New settings experience" in VS 2022 (UnifiedSettingsConstants.UsxFeatureEnabledUiContext of
        // Microsoft.VisualStudio.Shell.UI.Internal)
        public static bool ArePagesHidden => IsActive &&
            (VsVersionUtils.VsVersion >= VsVersion.VS2026 || NewSettingsExperienceContext.Value.IsActive);

        private static readonly Lazy<UIContext> NewSettingsExperienceContext = new Lazy<UIContext>(
            () => UIContext.FromUIContextGuid(new Guid("13b56f17-0b98-4a51-a9a0-9767d84796da")));

        // The descriptors with monikers, reusing those of earlier calls (cache)
        public static PropertyDescriptorCollection AddMonikers(PropertyDescriptorCollection properties,
            IDictionary<PropertyDescriptor, PropertyDescriptor> cache)
        {
            if (!IsSupported)
                return properties;

            var result = new PropertyDescriptor[properties.Count];
            lock (cache)
            {
                for (int i = 0; i < properties.Count; i++)
                {
                    PropertyDescriptor property = properties[i];
                    if (!cache.TryGetValue(property, out PropertyDescriptor withMoniker))
                    {
                        var setting = property.Attributes.OfType<UnifiedSettingAttribute>().SingleOrDefault();
                        withMoniker = setting == null ? property : new UnifiedSettingPropertyDescriptor(property, CreateMonikerAttribute(setting.Moniker));
                        cache[property] = withMoniker;
                    }
                    result[i] = withMoniker;
                }
            }
            return new PropertyDescriptorCollection(result, true);
        }

        private static Attribute CreateMonikerAttribute(string moniker)
        {
            return (Attribute)Activator.CreateInstance(MonikerAttributeType.Value, moniker);
        }

        private static Type FindMonikerAttributeType()
        {
            try
            {
                Assembly shell = typeof(DialogPage).Assembly;
                Type type = shell.GetType(PublicMonikerAttribute, false);
                if (type != null)
                    return type;

                AssemblyName framework = shell.GetReferencedAssemblies().FirstOrDefault(a => a.Name == InternalMonikerAttributeAssembly);
                return framework == null ? null : Assembly.Load(framework).GetType(InternalMonikerAttribute, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static UIContext FindUnifiedSettingsActiveContext()
        {
            try
            {
                return typeof(KnownUIContexts).GetProperty("UnifiedSettingsActiveContext", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as UIContext;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private class UnifiedSettingPropertyDescriptor : PropertyDescriptor
        {
            private readonly PropertyDescriptor _inner;

            public UnifiedSettingPropertyDescriptor(PropertyDescriptor inner, Attribute monikerAttribute)
                : base(inner, new[] { monikerAttribute })
            {
                _inner = inner;
            }

            public override Type ComponentType => _inner.ComponentType;
            public override bool IsReadOnly => _inner.IsReadOnly;
            public override Type PropertyType => _inner.PropertyType;
            public override TypeConverter Converter => _inner.Converter;

            public override bool CanResetValue(object component) => _inner.CanResetValue(component);
            public override object GetValue(object component) => _inner.GetValue(component);
            public override void ResetValue(object component) => _inner.ResetValue(component);
            public override bool ShouldSerializeValue(object component) => _inner.ShouldSerializeValue(component);

            // DialogPage saves an option when it changes (e.g. via DTE.Properties, or GTA's toolbar buttons), which the
            // pages notify with INotifyPropertyChanged
            public override bool SupportsChangeEvents => _inner.SupportsChangeEvents;
            public override void AddValueChanged(object component, EventHandler handler) => _inner.AddValueChanged(component, handler);
            public override void RemoveValueChanged(object component, EventHandler handler) => _inner.RemoveValueChanged(component, handler);

            public override void SetValue(object component, object value)
            {
                try
                {
                    _inner.SetValue(component, value);
                }
                catch (Exception e) when (ArePagesHidden && !(e is OutOfMemoryException))
                {
                    var cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    InvalidValueHandler?.Invoke(this, value, cause.Message);
                }
            }
        }
    }

}
