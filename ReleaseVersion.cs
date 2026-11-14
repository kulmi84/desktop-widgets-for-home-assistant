using System.Reflection;

[assembly: AssemblyProduct(ReleaseVersion.ProductName)]
[assembly: AssemblyVersion(ReleaseVersion.Version + ".0")]
[assembly: AssemblyFileVersion(ReleaseVersion.WindowsVersion)]
[assembly: AssemblyInformationalVersion(ReleaseVersion.Version + "+build." + ReleaseVersion.Build)]

internal static class ReleaseVersion
{
    internal const string ProductName = "Desktop Widgets for Home Assistant";
    internal const string SolarName = "Solar Widget Home Assistant";
    internal const string Version = "1.0.0";
    internal const string Build = "1411.55";
    // Windows file versions have four numeric components.
    internal const string WindowsVersion = "1.0.0.1466";
    internal const string Display = "Version " + Version + " \u00B7 Build " + Build;
}