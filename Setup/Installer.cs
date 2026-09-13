using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace HWWidget.Setup;

static class Installer
{
    const string AppName = "HW Widget";
    const string RunValue = "HWWidget";

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool MoveFileEx(string existing, string? newName, uint flags);

    const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x4;

    public static string TargetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "HWWidget");

    public static string TargetExe => Path.Combine(TargetDir, "HWWidget.exe");
    static string SetupCopy => Path.Combine(TargetDir, "HWWidgetSetup.exe");
    static string SettingsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HWWidget");

    public static bool Installed => File.Exists(TargetExe);

    public static void Install(bool startup, bool desktop, bool copySettings, out string report)
    {
        Directory.CreateDirectory(TargetDir);

        using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream("HWWidget.exe")
                         ?? throw new InvalidOperationException("Payload dell'applicazione mancante."))
        using (var dst = File.Create(TargetExe))
            src.CopyTo(dst);

        // keep a copy of the installer so "uninstall" keeps working after the original is deleted
        File.Copy(Environment.ProcessPath!, SetupCopy, true);

        string extra = "";
        if (copySettings)
        {
            int copied = 0;
            try
            {
                Directory.CreateDirectory(SettingsDir);
                foreach (var f in Directory.GetFiles(SettingsDir, "settings*.json"))
                {
                    File.Copy(f, Path.Combine(SettingsDir, Path.GetFileName(f)), true);
                    copied++;
                }
            }
            catch { }
            extra = copied > 0 ? $" · configurazione copiata ({copied} file)" : " · nessuna configurazione da copiare";
        }

        MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppName}.lnk"), TargetExe, TargetDir);
        if (desktop)
            MakeShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk"), TargetExe, TargetDir);

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
        {
            if (startup) k?.SetValue(RunValue, $"\"{TargetExe}\"");
            else k?.DeleteValue(RunValue, false);
        }

        using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWWidget"))
        {
            k?.SetValue("DisplayName", AppName);
            k?.SetValue("DisplayVersion", "1.0");
            k?.SetValue("Publisher", "HW Widget");
            k?.SetValue("DisplayIcon", TargetExe);
            k?.SetValue("InstallLocation", TargetDir);
            k?.SetValue("UninstallString", $"\"{SetupCopy}\" --uninstall");
            k?.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        report = $"Installato in {TargetDir}{(startup ? " · avvio con Windows attivo" : "")}" +
                 (desktop ? " · collegamento sul desktop" : "") + extra;
    }

    public static void Uninstall(bool silent)
    {
        if (!silent)
        {
            var answer = MessageBox.Show(
                "Rimuovere HW Widget da questo PC?\n\nI widget verranno chiusi e i file dell'applicazione cancellati. " +
                "Le tue impostazioni in %APPDATA%\\HWWidget restano.",
                "Disinstalla HW Widget", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
        }

        try
        {
            foreach (var p in Process.GetProcessesByName("HWWidget")) { try { p.Kill(); } catch { } }
        }
        catch { }

        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            k?.DeleteValue(RunValue, false);
        }
        catch { }

        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HWWidget", false); }
        catch { }

        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{AppName}.lnk"));
        TryDelete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk"));

        try
        {
            foreach (var f in Directory.GetFiles(TargetDir))
            {
                // the running installer copy can be renamed but not deleted
                if (string.Equals(f, SetupCopy, StringComparison.OrdinalIgnoreCase))
                {
                    string tempCopy = Path.Combine(Path.GetTempPath(), "HWWidgetSetup_tmp.exe");
                    try
                    {
                        File.Move(f, tempCopy, true);
                        MoveFileEx(tempCopy, null, MOVEFILE_DELAY_UNTIL_REBOOT);
                    }
                    catch { MoveFileEx(f, null, MOVEFILE_DELAY_UNTIL_REBOOT); }
                    continue;
                }
                TryDelete(f);
            }
            Directory.Delete(TargetDir, true);
            MoveFileEx(TargetDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
        catch { }

        if (!silent)
            MessageBox.Show($"HW Widget rimosso.\n\nRestano solo le impostazioni personali in:\n{SettingsDir}",
                "Disinstalla HW Widget", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    static void MakeShortcut(string linkPath, string target, string workDir)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = target;
            link.WorkingDirectory = workDir;
            link.IconLocation = target;
            link.Description = "HW Widget — monitor di sistema in stile acrylic";
            link.Save();
        }
        catch { }
    }
}
