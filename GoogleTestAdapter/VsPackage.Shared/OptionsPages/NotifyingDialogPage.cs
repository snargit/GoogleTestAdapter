using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace GoogleTestAdapter.VsPackage.OptionsPages
{

    // DTE.Properties("Google Test Adapter", <page>) returns the pages via COM, which requires them (and their base
    // classes) to be COM visible; the assembly is not.
    // DialogPage loads and saves the properties TypeDescriptor provides for the page: the pages provide them with the
    // monikers of Visual Studio's Unified Settings (see UnifiedSettingsBridge).
    [ComVisible(true)]
    public abstract class NotifyingDialogPage : DialogPage, INotifyPropertyChanged, ICustomTypeDescriptor
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly Dictionary<PropertyDescriptor, PropertyDescriptor> _unifiedSettingsProperties =
            new Dictionary<PropertyDescriptor, PropertyDescriptor>();

        protected void SetAndNotify<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region ICustomTypeDescriptor: the defaults, with the properties of Unified Settings

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() =>
            UnifiedSettingsBridge.AddMonikers(TypeDescriptor.GetProperties(this, true), _unifiedSettingsProperties);

        PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[] attributes) =>
            UnifiedSettingsBridge.AddMonikers(TypeDescriptor.GetProperties(this, attributes, true), _unifiedSettingsProperties);

        AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);
        string ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);
        string ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);
        TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);
        EventDescriptor ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
        PropertyDescriptor ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);
        object ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);
        EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);
        EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);
        object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor pd) => this;

        #endregion
    }

}
