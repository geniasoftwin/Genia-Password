using GeniaPassword.Models;
using GeniaPassword.UI;
using GeniaPassword.Security;

namespace GeniaPassword.Forms;

internal sealed class PasswordHistoryDialog : Form
{
    private readonly VaultEntry _entry;
    private readonly Action<string> _copyPassword;
    private readonly DataGridView _grid = new();
    private readonly CheckBox _showPasswordsBox = new() { Text = "Показывать старые пароли", AutoSize = true };
    private readonly Button _copyButton = new() { Text = "Копировать выбранный", AutoSize = true, Tag = UiTheme.AccentTag };

    internal PasswordHistoryDialog(VaultEntry entry, Action<string> copyPassword)
    {
        // MainForm передаёт уже отделённый от VaultService снимок записи; повторно
        // не клонируем секретные строки без необходимости.
        _entry = entry;
        _copyPassword = copyPassword;
        ConfigureForm();
        ConfigureGrid();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        FillRows();
        WireEvents();
    }

    private void ConfigureForm()
    {
        Text = $"История паролей — {_entry.Title}";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(650, 380);
        Size = new Size(760, 460);
        Font = UiTheme.Font(10F);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = UiTheme.Surface;
        _grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ActiveSince",
            HeaderText = "Действовал с",
            Width = 165
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Replaced",
            HeaderText = "Заменён",
            Width = 165
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Password",
            HeaderText = "Пароль",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
    }

    private void BuildLayout()
    {
        var description = new Label
        {
            Text = "Хранятся максимум 5 предыдущих паролей. История находится внутри зашифрованного vault.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(700, 0),
            Margin = new Padding(0, 0, 0, 10)
        };

        var closeButton = new Button
        {
            Text = "Закрыть",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_copyButton);
        buttons.Controls.Add(_showPasswordsBox);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(description, 0, 0);
        layout.Controls.Add(_grid, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        CancelButton = closeButton;
    }

    private void FillRows()
    {
        _grid.Rows.Clear();
        foreach (PasswordHistoryItem item in _entry.PasswordHistory
                     .OrderByDescending(item => item.ReplacedUtc))
        {
            int rowIndex = _grid.Rows.Add(
                FormatDate(item.ActiveSinceUtc),
                FormatDate(item.ReplacedUtc),
                DisplayPassword(item.Password));
            _grid.Rows[rowIndex].Tag = item;
        }

        bool hasHistory = _grid.Rows.Count > 0;
        _copyButton.Enabled = hasHistory;
        _showPasswordsBox.Enabled = hasHistory;
        if (hasHistory)
        {
            _grid.Rows[0].Selected = true;
        }
    }

    private void WireEvents()
    {
        _showPasswordsBox.CheckedChanged += (_, _) => RefreshPasswordCells();
        _copyButton.Click += (_, _) => CopySelectedPassword();
        _grid.DoubleClick += (_, _) => CopySelectedPassword();
        _grid.SelectionChanged += (_, _) => _copyButton.Enabled = _grid.SelectedRows.Count > 0;
    }

    private void RefreshPasswordCells()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is PasswordHistoryItem item)
            {
                row.Cells["Password"].Value = DisplayPassword(item.Password);
            }
        }
    }

    private string DisplayPassword(string password) =>
        _showPasswordsBox.Checked ? password : new string('•', Math.Clamp(password.Length, 8, 24));

    private void CopySelectedPassword()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not PasswordHistoryItem item)
        {
            return;
        }

        _copyPassword(item.Password);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value == default ? "—" : value.ToLocalTime().ToString("g");
}
