using GeniaPassword.Security;
using GeniaPassword.Services;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class UnlockDialog : Form
{
    private readonly VaultService _vault;
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly Button _unlockButton = new() { Text = "Открыть", AutoSize = true, Tag = UiTheme.AccentTag };
    private readonly CheckBox _showPasswordBox = new() { Text = "Показать пароль", AutoSize = true };

    internal UnlockDialog(VaultService vault)
    {
        _vault = vault;

        ConfigureForm();
        SecureInput.ConfigurePasswordBox(_passwordBox, MasterPasswordPolicy.MaximumLength);
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        WireEvents();
    }

    #region 1. Инициализация и настройка UI

    private void ConfigureForm()
    {
        Text = "Разблокировка";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 236);
        Font = UiTheme.Font(10F);
    }

    private void BuildLayout()
    {
        var titleLabel = new Label
        {
            Text = "Введите мастер-пароль",
            Font = UiTheme.Font(12F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitleLabel = new Label
        {
            Text = "Ваше хранилище зашифровано и защищено.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };

        var passwordLabel = new Label
        {
            Text = "Мастер-пароль:",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        _passwordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwordBox.Margin = new Padding(0, 2, 0, 8);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 6,
            ColumnCount = 1
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(subtitleLabel, 0, 1);
        layout.Controls.Add(passwordLabel, 0, 2);
        layout.Controls.Add(_passwordBox, 0, 3);
        layout.Controls.Add(_showPasswordBox, 0, 4);
        layout.Controls.Add(BuildButtonsPanel(), 0, 5);

        Controls.Add(layout);
    }

    private FlowLayoutPanel BuildButtonsPanel()
    {
        var cancelButton = new Button
        {
            Text = "Закрыть",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };

        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(_unlockButton);

        AcceptButton = _unlockButton;
        CancelButton = cancelButton;

        return buttonsPanel;
    }

    private void WireEvents()
    {
        Shown += (_, _) => _passwordBox.Focus();

        _showPasswordBox.CheckedChanged += (_, _) =>
            _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;

        _unlockButton.Click += Unlock;

        // Гарантированно очищаем поле при закрытии окна
        FormClosed += (_, _) => _passwordBox.Clear();
    }

    #endregion

    #region 2. Логика разблокировки

    private void Unlock(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_passwordBox.Text))
        {
            return;
        }

        try
        {
            UseWaitCursor = true;
            _unlockButton.Enabled = false;

            _vault.Unlock(_passwordBox.Text);

            if (_vault.NeedsSecurityUpgrade && !_vault.TryUpgradeSecurity(out string? upgradeError))
            {
                MessageBox.Show(
                    "Хранилище успешно открыто, но автоматическое усиление защиты не удалось. " +
                    "Данные доступны; повторная попытка произойдёт при следующем сохранении.\n\n" + upgradeError,
                    "Усиление защиты",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            _passwordBox.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (VaultUnlockException exception)
        {
            MessageBox.Show(
                exception.Message,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            _passwordBox.Clear();
            _passwordBox.Focus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось прочитать хранилище.\n\n{exception.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _unlockButton.Enabled = true;
        }
    }

    #endregion
}
