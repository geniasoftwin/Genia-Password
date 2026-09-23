using System.Diagnostics;
using GeniaPassword.Services;
using GeniaPassword.UI;
using GeniaPassword.Security;

namespace GeniaPassword.Forms;

internal enum VaultManagementAction
{
    None,
    ChangeMasterPassword,
    Audit,
    Deduplicate
}

internal sealed class VaultManagementDialog : Form
{
    private readonly VaultService _vault;

    internal VaultManagementAction RequestedAction { get; private set; }

    internal VaultManagementDialog(VaultService vault)
    {
        _vault = vault;
        ConfigureForm();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
    }

    private void ConfigureForm()
    {
        Text = "Управление";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 585);
        Font = UiTheme.Font(10F);
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Управление GeniaPassword",
            AutoSize = true,
            Font = UiTheme.Font(17F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitle = new Label
        {
            Text = "Безопасность, состояние хранилища и обслуживание.",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 16)
        };

        var infoPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 9,
            BackColor = UiTheme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 14)
        };
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        infoPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddInfoRow(infoPanel, 0, "Защита", _vault.SecuritySummary);
        AddInfoRow(infoPanel, 1, "Ревизия", _vault.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddInfoRow(infoPanel, 2, "Записей", _vault.EntryCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        AddInfoRow(infoPanel, 3, "Резервных копий", $"{_vault.BackupCount} из 10");
        AddInfoRow(infoPanel, 4, "Автоблокировка", "5 минут бездействия");
        AddInfoRow(infoPanel, 5, "Буфер обмена", "очистка через 30 секунд");
        AddInfoRow(infoPanel, 6, "Режим", "полностью офлайн");
        AddInfoRow(infoPanel, 7, "Хранилище", _vault.VaultPath);
        AddInfoRow(infoPanel, 8, "Версия", Application.ProductVersion);

        var actionsTitle = new Label
        {
            Text = "Действия",
            AutoSize = true,
            Font = UiTheme.Font(11F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 2, 0, 8)
        };

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0)
        };

        Button changePasswordButton = CreateActionButton("Сменить мастер-пароль", VaultManagementAction.ChangeMasterPassword);
        Button auditButton = CreateActionButton("Локальная проверка безопасности", VaultManagementAction.Audit);
        Button deduplicateButton = CreateActionButton("Убрать полные дубли", VaultManagementAction.Deduplicate);
        var backupsButton = new Button { Text = "Открыть резервные копии", AutoSize = true };
        UiTheme.StyleButton(backupsButton);
        backupsButton.Click += (_, _) => OpenBackupDirectory();
        foreach (Button button in new[] { changePasswordButton, auditButton, deduplicateButton, backupsButton })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Top;
            button.Height = UiTheme.ButtonHeight;
            button.Margin = new Padding(0, 0, 0, 8);
        }

        actions.Controls.Add(changePasswordButton, 0, 0);
        actions.Controls.Add(auditButton, 0, 1);
        actions.Controls.Add(deduplicateButton, 0, 2);
        actions.Controls.Add(backupsButton, 0, 3);

        var closeButton = new Button
        {
            Text = "Закрыть",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        UiTheme.StyleButton(closeButton);

        var bottom = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        bottom.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 6,
            BackColor = UiTheme.Window
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(infoPanel, 0, 2);
        layout.Controls.Add(actionsTitle, 0, 3);
        layout.Controls.Add(actions, 0, 4);
        layout.Controls.Add(bottom, 0, 5);

        Controls.Add(layout);
        CancelButton = closeButton;
    }

    private void OpenBackupDirectory()
    {
        try
        {
            Directory.CreateDirectory(_vault.BackupDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _vault.BackupDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось открыть папку резервных копий.\n\n{exception.Message}",
                "Резервные копии",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private Button CreateActionButton(string text, VaultManagementAction action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true
        };
        UiTheme.StyleButton(button);
        button.Click += (_, _) =>
        {
            RequestedAction = action;
            DialogResult = DialogResult.OK;
        };
        return button;
    }

    private static void AddInfoRow(TableLayoutPanel panel, int row, string name, string value)
    {
        panel.Controls.Add(new Label
        {
            Text = name,
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 5, 10, 5)
        }, 0, row);

        var valueLabel = new Label
        {
            Text = value,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 5, 0, 5)
        };
        panel.Controls.Add(valueLabel, 1, row);
    }
}
