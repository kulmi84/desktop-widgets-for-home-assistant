using System.Reflection;

[assembly: AssemblyProduct(ReleaseVersion.ProductName)]
[assembly: AssemblyVersion(ReleaseVersion.Version + ".0")]
[assembly: AssemblyFileVersion(ReleaseVersion.WindowsVersion)]
[assembly: AssemblyInformationalVersion(ReleaseVersion.Version + "+build." + ReleaseVersion.Build)]

internal static class ReleaseVersion
{
    internal const string ProductName = "Desktop Widgets for Home Assistant";
    internal const string SolarName = "Solar Widget Home Assistant";
    internal const string Version = "1.0.1";
    internal const string Build = "1511.2";
    internal const string WindowsVersion = Version + ".0";
    internal const string Display = "Version " + Version + " \u00B7 Build " + Build;
#if STORE
    internal const string DataFolder = "DesktopWidgetsForHomeAssistant.Store";
#else
    internal const string DataFolder = "SolarKachel";
#endif
}
