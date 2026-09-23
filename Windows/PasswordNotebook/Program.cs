using System.Security.Cryptography;
using System.Text;
using GeniaPassword.Forms;
using GeniaPassword.Security;
using GeniaPassword.Services;
using GeniaPassword.UI;

namespace GeniaPassword;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(UiTheme.Font(10F));

        string instanceId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppContext.BaseDirectory))))[..16];
        using var mutex = new Mutex(true, $"Local\\GeniaPassword_{instanceId}", out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Эта копия записной книжки уже открыта.",
                AppBrand.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            string vaultPath = Path.Combine(AppContext.BaseDirectory, "vault.pnb");
            if (!File.Exists(vaultPath) && !TryRestoreMissingVault(vaultPath))
            {
                return;
            }

            using var vault = new VaultService(vaultPath);

            if (File.Exists(vaultPath))
            {
                using var unlockDialog = new UnlockDialog(vault);
                if (unlockDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }
            else
            {
                using var createDialog = new CreateVaultDialog(vault);
                if (createDialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            Application.Run(new MainForm(vault));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Приложение завершилось из-за ошибки.\n\n{exception.Message}",
                AppBrand.Name,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool TryRestoreMissingVault(string vaultPath)
    {
        string backupPath = $"{vaultPath}.bak";
        string backupDirectory = Path.Combine(Path.GetDirectoryName(vaultPath) ?? AppContext.BaseDirectory, "backups");

        var candidates = new List<(string Path, string Description)>();
        if (File.Exists(backupPath))
        {
            candidates.Add((backupPath, "vault.pnb.bak"));
        }

        if (Directory.Exists(backupDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(backupDirectory, "vault_*.pnb")
                         .OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
            {
                candidates.Add((path, $"резервная копия {Path.GetFileName(path)}"));
            }
        }

        foreach ((string path, string description) in candidates)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (info.Length <= 0 || info.Length > VaultCrypto.MaximumEnvelopeBytes)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            DialogResult restore = MessageBox.Show(
                $"Основной файл vault.pnb не найден. Обнаружена {description}.\n\n" +
                "Восстановить основной файл из этой резервной копии?",
                AppBrand.Name,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button1);

            if (restore != DialogResult.Yes)
            {
                return false;
            }

            File.Copy(path, vaultPath, overwrite: false);
            return true;
        }

        return true;
    }

}
