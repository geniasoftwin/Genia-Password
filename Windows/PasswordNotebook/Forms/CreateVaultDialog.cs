using GeniaPassword.Security;
using GeniaPassword.Services;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class CreateVaultDialog(VaultService vault) : Form
{
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true };
    private readonly TextBox _confirmationBox = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _showPasswordBox = new() { Text = "Показать пароль", AutoSize = true };
    private readonly Button _createButton = new() { Text = "Создать", AutoSize = true, Tag = UiTheme.AccentTag };
    private readonly Label _strengthLabel = new() { AutoSize = true, Font = UiTheme.Font(9F, FontStyle.Bold) };

    #region 1. Инициализация и настройка UI

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ConfigureForm();
        SecureInput.ConfigurePasswordBox(_passwordBox, MasterPasswordPolicy.MaximumLength);
        SecureInput.ConfigurePasswordBox(_confirmationBox, MasterPasswordPolicy.MaximumLength);
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        WireEvents();
        UpdatePasswordStrength();
    }

    private void ConfigureForm()
    {
        Text = "Создание хранилища";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 350);
        Font = UiTheme.Font(10F);
    }

    private void BuildLayout()
    {
        var titleLabel = new Label
        {
            Text = "Придумайте мастер-пароль",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        };

        var descriptionLabel = new Label
        {
            Text = "Он шифрует все записи. Минимум 8 символов. Для лучшей защиты рекомендуем 12–16+ символов или длинную уникальную парольную фразу.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 4, 0, 8)
        };

        var warningLabel = new Label
        {
            Text = "Важно: восстановить забытый мастер-пароль невозможно.",
            ForeColor = Color.DarkRed,
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 8, 0, 8)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            RowCount = 7,
            ColumnCount = 1
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(descriptionLabel, 0, 1);
        layout.Controls.Add(BuildFieldsPanel(), 0, 2);
        layout.Controls.Add(_strengthLabel, 0, 3);
        layout.Controls.Add(_showPasswordBox, 0, 4);
        layout.Controls.Add(warningLabel, 0, 5);
        layout.Controls.Add(BuildButtonsPanel(), 0, 6);

        Controls.Add(layout);
    }

    private TableLayoutPanel BuildFieldsPanel()
    {
        var fields = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 4)
        };

        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        fields.Controls.Add(new Label { Text = "Мастер-пароль:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 14, 0) }, 0, 0);
        fields.Controls.Add(_passwordBox, 1, 0);
        fields.Controls.Add(new Label { Text = "Повторите:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 14, 0) }, 0, 1);
        fields.Controls.Add(_confirmationBox, 1, 1);

        // Растягиваем поля только по горизонтали. Без Top/Bottom/Dock=Fill
        // однострочный native TextBox сохраняет стандартную DPI-aware высоту и
        // визуально центрируется по вертикали внутри строки TableLayoutPanel.
        _passwordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _confirmationBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwordBox.Margin = new Padding(0, 4, 0, 4);
        _confirmationBox.Margin = new Padding(0, 4, 0, 4);

        return fields;
    }

    private FlowLayoutPanel BuildButtonsPanel()
    {
        var cancelButton = new Button
        {
            Text = "Отмена",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttonsPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };

        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(_createButton);

        AcceptButton = _createButton;
        CancelButton = cancelButton;

        return buttonsPanel;
    }

    private void WireEvents()
    {
        Shown += (_, _) => _passwordBox.Focus();

        _passwordBox.TextChanged += (_, _) => UpdatePasswordStrength();

        _showPasswordBox.CheckedChanged += (_, _) =>
        {
            _passwordBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
            _confirmationBox.UseSystemPasswordChar = !_showPasswordBox.Checked;
        };

        _createButton.Click += CreateVault;

        FormClosed += (_, _) =>
        {
            _passwordBox.Clear();
            _confirmationBox.Clear();
        };
    }

    #endregion

    #region 2. Оценка сложности пароля

    private void UpdatePasswordStrength()
    {
        string password = _passwordBox.Text;

        if (password.Length == 0)
        {
            _strengthLabel.Text = $"Введите пароль (минимум {MasterPasswordPolicy.MinimumLength} символов)";
            _strengthLabel.ForeColor = SystemColors.GrayText;
            return;
        }

        PasswordStrengthLevel strength = PasswordSecurity.EvaluateStrength(password);
        switch (strength)
        {
            case PasswordStrengthLevel.TooShort:
                _strengthLabel.Text = $"Слишком короткий ({password.Length}/{MasterPasswordPolicy.MinimumLength})";
                _strengthLabel.ForeColor = Color.DarkRed;
                break;
            case PasswordStrengthLevel.Weak:
                _strengthLabel.Text = "Слабый — лучше использовать несколько случайных слов или более длинную фразу";
                _strengthLabel.ForeColor = Color.DarkOrange;
                break;
            case PasswordStrengthLevel.Good:
                _strengthLabel.Text = "Хороший пароль";
                _strengthLabel.ForeColor = Color.DarkGoldenrod;
                break;
            case PasswordStrengthLevel.Strong:
                _strengthLabel.Text = "Сильный пароль";
                _strengthLabel.ForeColor = Color.SeaGreen;
                break;
            default:
                _strengthLabel.Text = "Отличная длинная парольная фраза";
                _strengthLabel.ForeColor = Color.SeaGreen;
                break;
        }
    }

    #endregion

    #region 3. Логика создания хранилища

    private void CreateVault(object? sender, EventArgs e)
    {
        if (!MasterPasswordPolicy.TryValidateForCreation(_passwordBox.Text, out string validationMessage))
        {
            MessageBox.Show(
                validationMessage,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _passwordBox.SelectAll();
            _passwordBox.Focus();
            return;
        }

        if (!string.Equals(_passwordBox.Text, _confirmationBox.Text, StringComparison.Ordinal))
        {
            MessageBox.Show(
                "Введённые пароли не совпадают.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _confirmationBox.SelectAll();
            _confirmationBox.Focus();
            return;
        }

        try
        {
            UseWaitCursor = true;
            _createButton.Enabled = false;

            vault.Create(_passwordBox.Text);

            _passwordBox.Clear();
            _confirmationBox.Clear();
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось создать хранилище. Убедитесь, что папка программы доступна для записи.\n\n{exception.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _createButton.Enabled = true;
        }
    }

    #endregion
}
