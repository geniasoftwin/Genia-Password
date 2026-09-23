using GeniaPassword.Security;
using GeniaPassword.Services;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class ChangeMasterPasswordDialog(VaultService vault) : Form
{
    private readonly TextBox _currentBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _newBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirmBox = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _showBox = new() { Text = "Показать пароли", AutoSize = true };
    private readonly Label _strengthLabel = new() { AutoSize = true, Font = UiTheme.Font(9F, FontStyle.Bold) };
    private readonly Button _changeButton = new() { Text = "Сменить пароль", AutoSize = true, Tag = UiTheme.AccentTag };

    internal bool BackupUpdated { get; private set; } = true;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ConfigureForm();
        SecureInput.ConfigurePasswordBox(_currentBox, MasterPasswordPolicy.MaximumLength);
        SecureInput.ConfigurePasswordBox(_newBox, MasterPasswordPolicy.MaximumLength);
        SecureInput.ConfigurePasswordBox(_confirmBox, MasterPasswordPolicy.MaximumLength);
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        WireEvents();
        UpdateStrength();
    }

    private void ConfigureForm()
    {
        Text = "Смена мастер-пароля";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(530, 390);
        Font = UiTheme.Font(10F);
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Сменить мастер-пароль",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold)
        };

        var description = new Label
        {
            Text = "Новый мастер-пароль защитит VaultKey. После успешной смены основной vault и резервная копия будут привязаны к новому паролю.",
            AutoSize = true,
            MaximumSize = new Size(470, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 12)
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, "Текущий пароль:", _currentBox, 0);
        AddField(fields, "Новый пароль:", _newBox, 1);
        AddField(fields, "Повторите новый:", _confirmBox, 2);

        var cancelButton = new Button
        {
            Text = "Отмена",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_changeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(description, 0, 1);
        layout.Controls.Add(fields, 0, 2);
        layout.Controls.Add(_strengthLabel, 0, 3);
        layout.Controls.Add(_showBox, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        AcceptButton = _changeButton;
        CancelButton = cancelButton;
    }

    private static void AddField(TableLayoutPanel panel, string label, TextBox box, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 14, 0)
        }, 0, row);
        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(box, 1, row);
    }

    private void WireEvents()
    {
        Shown += (_, _) => _currentBox.Focus();
        _newBox.TextChanged += (_, _) => UpdateStrength();
        _showBox.CheckedChanged += (_, _) =>
        {
            bool hide = !_showBox.Checked;
            _currentBox.UseSystemPasswordChar = hide;
            _newBox.UseSystemPasswordChar = hide;
            _confirmBox.UseSystemPasswordChar = hide;
        };
        _changeButton.Click += ChangePassword;
        FormClosed += (_, _) =>
        {
            _currentBox.Clear();
            _newBox.Clear();
            _confirmBox.Clear();
        };
    }

    private void UpdateStrength()
    {
        string password = _newBox.Text;
        if (password.Length == 0)
        {
            _strengthLabel.Text = $"Новый пароль: минимум {MasterPasswordPolicy.MinimumLength} символов";
            _strengthLabel.ForeColor = SystemColors.GrayText;
            return;
        }

        PasswordStrengthLevel level = PasswordSecurity.EvaluateStrength(password);
        (_strengthLabel.Text, _strengthLabel.ForeColor) = level switch
        {
            PasswordStrengthLevel.TooShort => ($"Слишком короткий ({password.Length}/{MasterPasswordPolicy.MinimumLength})", Color.DarkRed),
            PasswordStrengthLevel.Weak => ("Слабый пароль", Color.DarkOrange),
            PasswordStrengthLevel.Good => ("Хороший пароль", Color.DarkGoldenrod),
            PasswordStrengthLevel.Strong => ("Сильный пароль", Color.SeaGreen),
            _ => ("Отличная длинная парольная фраза", Color.SeaGreen)
        };
    }

    private void ChangePassword(object? sender, EventArgs e)
    {
        if (!string.Equals(_newBox.Text, _confirmBox.Text, StringComparison.Ordinal))
        {
            MessageBox.Show("Новые пароли не совпадают.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _confirmBox.SelectAll();
            _confirmBox.Focus();
            return;
        }

        if (!MasterPasswordPolicy.TryValidateForCreation(_newBox.Text, out string validationMessage))
        {
            MessageBox.Show(validationMessage, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _newBox.SelectAll();
            _newBox.Focus();
            return;
        }

        if (string.Equals(_currentBox.Text, _newBox.Text, StringComparison.Ordinal))
        {
            MessageBox.Show("Новый мастер-пароль совпадает с текущим.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            UseWaitCursor = true;
            _changeButton.Enabled = false;
            BackupUpdated = vault.ChangeMasterPassword(_currentBox.Text, _newBox.Text);
            _currentBox.Clear();
            _newBox.Clear();
            _confirmBox.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (VaultUnlockException exception)
        {
            MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _currentBox.Clear();
            _currentBox.Focus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось сменить мастер-пароль. Исходное хранилище сохранено.\n\n{exception.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _changeButton.Enabled = true;
        }
    }
}
