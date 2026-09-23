using GeniaPassword.Security;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class PasswordGeneratorDialog : Form
{
    private readonly TextBox _previewBox = new()
    {
        ReadOnly = true,
        ShortcutsEnabled = false
    };

    private readonly NumericUpDown _lengthBox = new()
    {
        Minimum = 8,
        Maximum = 64,
        Value = 18,
        Width = 82,
        TextAlign = HorizontalAlignment.Center
    };

    private readonly CheckBox _upperBox = new() { Text = "Заглавные A–Z", Checked = true, AutoSize = true };
    private readonly CheckBox _digitsBox = new() { Text = "Цифры 0–9", Checked = true, AutoSize = true };
    private readonly CheckBox _symbolsBox = new() { Text = "Спецсимволы", Checked = true, AutoSize = true };
    private readonly CheckBox _ambiguousBox = new() { Text = "Исключить похожие O/0, l/1", Checked = true, AutoSize = true, Margin = new Padding(0, 3, 0, 0) };
    private readonly Label _summaryLabel = new() { AutoSize = true, ForeColor = UiTheme.Muted };

    internal string GeneratedPassword { get; private set; } = string.Empty;

    internal PasswordGeneratorDialog()
    {
        ConfigureForm();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        WireEvents();
        Regenerate();
    }

    private void ConfigureForm()
    {
        Text = "Генератор пароля";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 470);
        Font = UiTheme.Font(10F);
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Генератор пароля",
            AutoSize = true,
            Font = UiTheme.Font(16F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 0, 0, 4)
        };

        var subtitle = new Label
        {
            Text = "Настройте состав пароля. Генерация выполняется локально.",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 14)
        };

        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 0, 14),
            BackColor = UiTheme.SurfaceAlt
        };
        _previewBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _previewBox.Font = UiTheme.Font(11F);
        _previewBox.Margin = new Padding(0, 0, 0, 6);
        previewPanel.Controls.Add(_previewBox, 0, 0);
        previewPanel.Controls.Add(_summaryLabel, 0, 1);

        var lengthPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 8)
        };
        lengthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        lengthPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lengthPanel.Controls.Add(new Label
        {
            Text = "Длина",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = UiTheme.Font(10F, FontStyle.Bold)
        }, 0, 0);
        lengthPanel.Controls.Add(_lengthBox, 1, 0);

        var options = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0, 0, 0, 6)
        };
        options.Controls.Add(new Label
        {
            Text = "Строчные a–z включены всегда",
            AutoSize = true,
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 2, 0, 8)
        }, 0, 0);
        options.Controls.Add(_upperBox, 0, 1);
        options.Controls.Add(_digitsBox, 0, 2);
        options.Controls.Add(_symbolsBox, 0, 3);
        options.Controls.Add(_ambiguousBox, 0, 4);

        var regenerateButton = new Button
        {
            Text = "Сгенерировать ещё",
            AutoSize = true
        };
        regenerateButton.Click += (_, _) => Regenerate();

        var useButton = new Button
        {
            Text = "Использовать",
            AutoSize = true,
            Tag = UiTheme.AccentTag,
            DialogResult = DialogResult.OK
        };
        var cancelButton = new Button
        {
            Text = "Отмена",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 3,
            Margin = new Padding(0, 16, 0, 0)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // Keep all three actions vertically centered in the same button row.
        // Previously only the left button had a non-Top anchor, so WinForms
        // centered it while Cancel/Use remained top-aligned.
        regenerateButton.Anchor = AnchorStyles.Left;
        cancelButton.Anchor = AnchorStyles.Right;
        useButton.Anchor = AnchorStyles.Right;

        buttons.Controls.Add(regenerateButton, 0, 0);
        buttons.Controls.Add(cancelButton, 1, 0);
        buttons.Controls.Add(useButton, 2, 0);

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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(previewPanel, 0, 2);
        layout.Controls.Add(lengthPanel, 0, 3);
        layout.Controls.Add(options, 0, 4);
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        AcceptButton = useButton;
        CancelButton = cancelButton;
    }

    private void WireEvents()
    {
        _lengthBox.ValueChanged += (_, _) => Regenerate();
        _upperBox.CheckedChanged += (_, _) => Regenerate();
        _digitsBox.CheckedChanged += (_, _) => Regenerate();
        _symbolsBox.CheckedChanged += (_, _) => Regenerate();
        _ambiguousBox.CheckedChanged += (_, _) => Regenerate();
        FormClosed += (_, _) => _previewBox.Clear();
    }

    private void Regenerate()
    {
        GeneratedPassword = PasswordGenerator.Generate(
            decimal.ToInt32(_lengthBox.Value),
            _upperBox.Checked,
            _digitsBox.Checked,
            _symbolsBox.Checked,
            _ambiguousBox.Checked);

        _previewBox.Text = GeneratedPassword;
        _summaryLabel.Text = $"{GeneratedPassword.Length} символов • криптографический RNG";
    }
}
