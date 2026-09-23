using GeniaPassword.Security;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class PasswordAuditDialog : Form
{
    private readonly PasswordAuditReport _report;
    private readonly DataGridView _grid = new();
    private readonly Label _summaryLabel = new();
    private readonly Button _goToEntryButton = new() { Text = "Перейти к записи", AutoSize = true, Tag = UiTheme.AccentTag };

    internal Guid? SelectedEntryId { get; private set; }

    internal PasswordAuditDialog(PasswordAuditReport report)
    {
        _report = report;
        ConfigureForm();
        ConfigureGrid();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        FillReport();
        WireEvents();
    }

    private void ConfigureForm()
    {
        Text = "Аудит безопасности";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(760, 480);
        Size = new Size(920, 580);
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
        _grid.AutoGenerateColumns = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.Fixed3D;

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Severity",
            HeaderText = "Уровень",
            Width = 105,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Entry",
            HeaderText = "Запись",
            Width = 190,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Category",
            HeaderText = "Проверка",
            Width = 150,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Message",
            HeaderText = "Рекомендация",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void BuildLayout()
    {
        var titleLabel = new Label
        {
            Text = "Локальный аудит паролей",
            Font = UiTheme.Font(15F, FontStyle.Bold),
            AutoSize = true
        };

        var descriptionLabel = new Label
        {
            Text = "Проверка выполняется только в памяти приложения. Пароли и результаты никуда не отправляются.",
            ForeColor = SystemColors.GrayText,
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            Margin = new Padding(0, 3, 0, 10)
        };

        _summaryLabel.AutoSize = true;
        _summaryLabel.Margin = new Padding(0, 0, 0, 10);

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
            Margin = new Padding(0, 10, 0, 0)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(_goToEntryButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(descriptionLabel, 0, 1);
        layout.Controls.Add(_summaryLabel, 0, 2);
        layout.Controls.Add(_grid, 0, 3);
        layout.Controls.Add(buttons, 0, 4);

        Controls.Add(layout);
        CancelButton = closeButton;
    }

    private void FillReport()
    {
        if (_report.Findings.Count == 0)
        {
            _summaryLabel.Text = "Проблем не найдено. Повторяющихся, коротких или явно слабых паролей нет.";
            _summaryLabel.ForeColor = Color.SeaGreen;
            _goToEntryButton.Enabled = false;
            return;
        }

        _summaryLabel.Text =
            $"Затронуто записей: {_report.AffectedEntries}. " +
            $"Критично: {_report.CriticalCount}; высокий риск: {_report.HighCount}; " +
            $"предупреждения: {_report.WarningCount}.";

        foreach (PasswordAuditFinding finding in _report.Findings)
        {
            int rowIndex = _grid.Rows.Add(
                SeverityText(finding.Severity),
                finding.EntryTitle,
                finding.Category,
                finding.Message);
            _grid.Rows[rowIndex].Tag = finding;
        }

        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
        }
    }

    private void WireEvents()
    {
        _goToEntryButton.Click += (_, _) => SelectCurrentEntry();
        _grid.DoubleClick += (_, _) => SelectCurrentEntry();
        _grid.SelectionChanged += (_, _) => _goToEntryButton.Enabled = _grid.SelectedRows.Count > 0;
    }

    private void SelectCurrentEntry()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not PasswordAuditFinding finding)
        {
            return;
        }

        SelectedEntryId = finding.EntryId;
        DialogResult = DialogResult.OK;
    }

    private static string SeverityText(PasswordAuditSeverity severity) => severity switch
    {
        PasswordAuditSeverity.Critical => "Критично",
        PasswordAuditSeverity.High => "Высокий",
        PasswordAuditSeverity.Warning => "Внимание",
        _ => "Инфо"
    };
}
