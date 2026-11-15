using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string Title = ReleaseVersion.ProductName;
    const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SolarKachel";
    static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "SolarKachel");
    static readonly string Program = Path.Combine(InstallDir, "SolarKachel.exe");
    static readonly string Maintenance = Path.Combine(InstallDir, "SolarKachel-Setup.exe");
    static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Title + ".lnk");

    [STAThread]
    static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                using (var data = Payload())
                    return data.ReadByte() == 'M' && data.ReadByte() == 'Z' ? 0 : 1;
            }

            if (args.Length == 2 && args[0] == "--cleanup")
            {
                using (var parent = Process.GetProcessById(int.Parse(args[1])))
                    if (!parent.WaitForExit(10000)) throw new IOException("Das Deinstallationsprogramm wurde nicht beendet.");
                Uninstall();
                return 0;
            }

            if (args.Length == 1 && args[0] == "--uninstall")
            {
                if (MessageBox.Show(Title + " deinstallieren?", Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 0;
                EnsureClosed();
                if (string.Equals(Application.ExecutablePath, Maintenance, StringComparison.OrdinalIgnoreCase))
                {
                    string temporary = Path.Combine(Path.GetTempPath(), "SolarKachel-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Maintenance, temporary);
                    Process.Start(temporary, "--cleanup " + Process.GetCurrentProcess().Id);
                }
                else Uninstall();
                return 0;
            }

            if (MessageBox.Show(Title + " für dieses Windows-Konto installieren?", Title,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 0;
            EnsureClosed();
            Install();
            if (MessageBox.Show(Title + " wurde installiert. Jetzt starten?", Title,
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Process.Start(Program);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static Stream Payload()
    {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream("SolarKachel.exe");
    }

    static void EnsureClosed()
    {
        foreach (var process in Process.GetProcessesByName("SolarKachel"))
        {
            using (process)
            {
                if (string.Equals(process.MainModule.FileName, Program, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Bitte das installierte Solar Widget zuerst schließen.");
            }
        }
    }

    static void Install()
    {
        Directory.CreateDirectory(InstallDir);
        string temp = Path.Combine(InstallDir, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = Payload())
            using (var output = File.Create(temp)) input.CopyTo(output);
            if (File.Exists(Program)) File.Replace(temp, Program, null);
            else File.Move(temp, Program);
        }
        finally { File.Delete(temp); }
        if (!string.Equals(Application.ExecutablePath, Maintenance, StringComparison.OrdinalIgnoreCase))
            File.Copy(Application.ExecutablePath, Maintenance, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Shortcut));
        var shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link = ((dynamic)shell).CreateShortcut(Shortcut);
        link.TargetPath = Program;
        link.WorkingDirectory = InstallDir;
        link.Description = "Solarwerte aus Home Assistant";
        link.IconLocation = Program + ",0";
        link.Save();
        File.Delete(Path.Combine(Path.GetDirectoryName(Shortcut), "Home Assistant Widget.lnk"));
        File.Delete(Path.Combine(Path.GetDirectoryName(Shortcut), ReleaseVersion.SolarName + ".lnk"));
        File.Delete(Path.Combine(Path.GetDirectoryName(Shortcut), "Solar-Kachel.lnk"));
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
        {
            key.SetValue("DisplayName", Title);
            key.SetValue("DisplayVersion", ReleaseVersion.Version);
            key.SetValue("DisplayIcon", Program);
            key.SetValue("InstallLocation", InstallDir);
            key.SetValue("UninstallString", "\"" + Maintenance + "\" --uninstall");
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
        }
    }

    static void Uninstall()
    {
        File.Delete(Program);
        File.Delete(Maintenance);
        File.Delete(Shortcut);
        if (Directory.Exists(InstallDir) && Directory.GetFileSystemEntries(InstallDir).Length == 0)
            Directory.Delete(InstallDir);
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        MessageBox.Show(Title + " wurde deinstalliert.", Title);
    }
}
