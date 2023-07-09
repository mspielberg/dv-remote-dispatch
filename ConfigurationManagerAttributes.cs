/// <summary>
/// Class that specifies how a setting should be displayed inside the ConfigurationManager settings window.
/// 
/// Usage:
/// This class template has to be copied inside the plugin's project and referenced by its code directly.
/// make a new instance, assign any fields that you want to override, and pass it as a tag for your setting.
/// 
/// If a field is null (default), it will be ignored and won't change how the setting is displayed.
/// If a field is non-null (you assigned a value to it), it will override default behavior.
/// </summary>
/// 
/// <remarks> 
/// You can read more and see examples in the readme at https://github.com/BepInEx/BepInEx.ConfigurationManager
/// You can optionally remove fields that you won't use from this class, it's the same as leaving them null.
/// </remarks>
#pragma warning disable 0169, 0414, 0649
internal sealed class ConfigurationManagerAttributes
{
    /// <summary>
    /// Custom setting editor (OnGUI code that replaces the default editor provided by ConfigurationManager).
    /// See below for a deeper explanation. Using a custom drawer will cause many of the other fields to do nothing.
    /// </summary>
    public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
    
    /// <summary>
    /// Force the "Reset" button to not be displayed, even if a valid DefaultValue is available. 
    /// </summary>
    public bool? HideDefaultButton;
    
    /// <summary>
    /// Only show the value, don't allow editing it.
    /// </summary>
    public bool? ReadOnly;
    
    /// <summary>
    /// If true, don't show the setting by default. User has to turn on showing advanced settings or search for it.
    /// </summary>
    public bool? IsAdvanced;
}
