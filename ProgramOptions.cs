using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

sealed class ProgramOptions : Form
{
    internal const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string StartupName = "DesktopWidgetsForHomeAssistant";

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    protected override void OnHandleCreated(EventArgs args)
    {
        base.OnHandleCreated(args);
        int dark = 1;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
    }

    internal ProgramOptions(Func<Settings> getSettings, Action connect, Action restored, Action<bool> revealWidgets = null)
    {
        Text = ReleaseVersion.ProductName + UiText.Get(" – Programmeinstellungen", " – Application settings");
        Icon = WidgetIcon.Load();
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        ClientSize = new Size(486, 330);
        BackColor = SettingsTheme.Surface;
        ForeColor = Color.FromArgb(239, 239, 242);

        var connection = Group("Home Assistant", 12, 58);
        AddButton(connection, UiText.Get("Verbindung einrichten…", "Set up connection…"), 14, 20, 202, connect);
        var backup = Group(UiText.Get("Sicherung", "Backup"), 80, 58);
        AddButton(backup, UiText.Get("Einstellungen sichern…", "Back up settings…"), 14, 20, 202,
            () => ProfileBackup.Export(this, getSettings()));
        AddButton(backup, UiText.Get("Wiederherstellen…", "Restore…"), 226, 20, 212, () =>
        {
            if (!ProfileBackup.Import(this, getSettings())) return;
            DialogResult = DialogResult.OK;
            restored();
        });
        var startup = Group(UiText.Get("Allgemeine Einstellungen", "General settings"), 148, 126);
#if STORE
        AddButton(startup, UiText.Get("Windows-Autostart öffnen", "Open Windows startup settings"), 14, 19, 202, () =>
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch (Exception) { ShowStartupError(); }
        });
#else
        var checkbox = new CheckBox { Text = UiText.Get("Mit Windows starten", "Start with Windows"), AutoSize = true, Location = new Point(15, 23) };
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(StartupKey))
                checkbox.Checked = key != null && key.GetValue(StartupName) != null;
        }
        catch (Exception) { checkbox.Enabled = false; }
        bool saved = checkbox.Checked;
        bool updating = false;
        checkbox.CheckedChanged += delegate
        {
            if (updating) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(StartupKey))
                {
                    if (checkbox.Checked) key.SetValue(StartupName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(StartupName, false);
                }
                saved = checkbox.Checked;
            }
            catch (Exception)
            {
                updating = true;
                checkbox.Checked = saved;
                updating = false;
                ShowStartupError();
            }
        };
        startup.Controls.Add(checkbox);
#endif
        AddLanguagePicker(startup);
        var reveal = new CheckBox { Text = UiText.Get("Ausgeblendete Widgets zum Bearbeiten anzeigen",
            "Show hidden widgets for editing"), AutoSize = true, Checked = true, Location = new Point(15, 94) };
        reveal.CheckedChanged += delegate { if (revealWidgets != null) revealWidgets(reveal.Checked); };
        startup.Controls.Add(reveal);
        Shown += delegate { if (revealWidgets != null) revealWidgets(reveal.Checked); };
        FormClosed += delegate { if (revealWidgets != null) revealWidgets(false); };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        footer.Paint += delegate(object sender, PaintEventArgs args)
        {
            using (var line = new Pen(Color.FromArgb(63, 63, 69)))
                args.Graphics.DrawLine(line, 0, 0, footer.ClientSize.Width, 0);
        };
        footer.Controls.Add(new Label { Text = ReleaseVersion.Display, ForeColor = Color.FromArgb(172, 179, 190),
            AutoSize = true, Location = new Point(14, 15) });
        AddGitHubLink(footer);
        var close = AddButton(footer, UiText.Get("Schließen", "Close"), 372, 8, 100, () => Close());
        Controls.Add(footer);
        close.DialogResult = DialogResult.Cancel;
        CancelButton = close;
    }

    void AddLanguagePicker(Control group)
    {
        string caption = UiText.Get("Sprache", "Language");
        group.Controls.Add(new Label { Text = caption, TextAlign = ContentAlignment.MiddleLeft,
            Bounds = new Rectangle(14, 56, 90, 25) });
        var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
            BackColor = SettingsTheme.Input, ForeColor = ForeColor, AccessibleName = caption,
            Bounds = new Rectangle(108, 56, 220, 25) };
        picker.Items.AddRange(new object[] { UiText.Get("Windows-Sprache", "Windows language"), "Deutsch", "English" });
        string[] choices = { "system", "de", "en" };
        picker.SelectedIndex = Array.IndexOf(choices, UiText.Preference);
        picker.SelectionChangeCommitted += delegate
        {
            try
            {
                UiText.SavePreference(choices[picker.SelectedIndex]);
                MessageBox.Show(this, UiText.Get("Starte die App neu, um die Sprache zu wechseln.",
                    "Restart the app to change the language."), caption);
            }
            catch (System.IO.IOException) { ShowLanguageError(caption); }
            catch (UnauthorizedAccessException) { ShowLanguageError(caption); }
        };
        group.Controls.Add(picker);
    }

    void ShowLanguageError(string caption)
    {
        MessageBox.Show(this, UiText.Get("Die Sprache konnte nicht gespeichert werden.",
            "Could not save the language preference."), caption);
    }

    void AddGitHubLink(Control footer)
    {
        var profile = new LinkLabel { Text = "GitHub · kulmi84", AutoSize = true,
            LinkColor = Color.FromArgb(126, 190, 255), ActiveLinkColor = Color.White,
            Location = new Point(215, 15) };
        profile.LinkClicked += delegate
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/kulmi84") { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, UiText.Get("Das GitHub-Profil konnte nicht geöffnet werden.", "Could not open the GitHub profile."), Text);
            }
        };
        footer.Controls.Add(profile);
    }

    GroupBox Group(string text, int y, int height)
    {
        var group = new SettingsCard { Text = text, ForeColor = ForeColor, Bounds = new Rectangle(14, y, 458, height) };
        Controls.Add(group);
        return group;
    }

    Button AddButton(Control group, string label, int x, int y, int width, Action click)
    {
        var button = new SettingsButton { Text = label, Bounds = new Rectangle(x, y, width, 28),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(45, 45, 50), ForeColor = ForeColor };
        button.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 83);
        button.Click += delegate { click(); };
        group.Controls.Add(button);
        return button;
    }

    void ShowStartupError()
    {
        MessageBox.Show(this, UiText.Get("Die Autostart-Einstellung konnte nicht geöffnet oder gespeichert werden.", "Could not open or save the startup setting."), Text);
    }
}
