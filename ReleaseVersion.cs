using System.Reflection;

[assembly: AssemblyProduct(ReleaseVersion.ProductName)]
[assembly: AssemblyVersion(ReleaseVersion.Version + ".0")]
[assembly: AssemblyFileVersion(ReleaseVersion.WindowsVersion)]
[assembly: AssemblyInformationalVersion(ReleaseVersion.Version)]

internal static class ReleaseVersion
{
    internal const string ProductName = "Desktop Widgets for Home Assistant";
    internal const string SolarName = "Solar Widget Home Assistant";
    internal const string Version = "1.2.0";
    internal const string WindowsVersion = Version + ".0";
    internal const string Display = "Version " + Version;
#if STORE
    internal const string DataFolder = "DesktopWidgetsForHomeAssistant.Store";
#else
    internal const string DataFolder = "SolarKachel";
#endif
}