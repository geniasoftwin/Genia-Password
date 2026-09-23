using System.Runtime.InteropServices;
using Microsoft.Win32;
using GeniaPassword.Models;
using GeniaPassword.Security;
using GeniaPassword.Services;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class MainForm : Form
{
    private static readonly TimeSpan AutoLockAfter = TimeSpan.FromMinutes(5);
    private const double DefaultLeftPanelRatio = 0.34;
    private const int LeftPanelMinimumLogical = 350;
    private const int RightPanelMinimumLogical = 590;

    private readonly VaultService _vault;
    private readonly TextBox _searchBox = new() { PlaceholderText = "Поиск..." };
    private readonly ListBox _entriesList = new() { IntegralHeight = false };
    private readonly Label _titleLabel = new();
    private readonly CheckBox _showSecretsBox = new() { Text = "Показывать секреты", AutoSize = true };
    private readonly TableLayoutPanel _detailsTable = new();
    private readonly ToolStripStatusLabel _statusLabel = new() { Text = "Готово" };
    private readonly System.Windows.Forms.Timer _clipboardTimer = new() { Interval = 30_000 };
    private readonly System.Windows.Forms.Timer _autoLockTimer = new() { Interval = 15_000 };

    private uint _clipboardSequence;
    private bool _clipboardOwned;
    private readonly ToolTip _toolTip = new();
    private bool _isLocking;
    private bool _lockedBySystemEvent;
    private bool _systemEventsSubscribed;

    internal MainForm(VaultService vault)
    {
        _vault = vault;
        ConfigureForm();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        WireEvents();
        RefreshEntries();
        _autoLockTimer.Start();
        SetStatus(_vault.NeedsSecurityUpgrade
            ? "Vault открыт; усиление защиты будет повторено при сохранении"
            : $"Защита vault v2 • ревизия {_vault.Revision}");
    }

    private VaultEntry? SelectedEntry => _entriesList.SelectedItem as VaultEntry;

    private void ConfigureForm()
    {
        Text = AppBrand.Name;
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1000, 620);
        Size = new Size(1240, 780);
        Font = UiTheme.Font(10F);
        KeyPreview = true;
        BackColor = UiTheme.Window;
        ForeColor = UiTheme.Text;
    }

    private void BuildLayout()
    {
        var splitContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.None,
            Orientation = Orientation.Vertical,
            BackColor = UiTheme.Border,
            SplitterWidth = 6
        };
        splitContainer.Panel1.BackColor = UiTheme.SurfaceAlt;
        splitContainer.Panel2.BackColor = UiTheme.Window;

        splitContainer.Panel1.Controls.Add(BuildLeftPanel());
        splitContainer.Panel2.Controls.Add(BuildRightPanel());

        var statusStrip = new StatusStrip
        {
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Muted,
            SizingGrip = false
        };
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(splitContainer);
        Controls.Add(statusStrip);

        // Не задаём большие Panel1MinSize/Panel2MinSize в object initializer:
        // в этот момент SplitContainer ещё имеет служебный размер WinForms, и на части
        // DPI-конфигураций setter может проверить SplitterDistance против этой временной
        // ширины и выбросить ArgumentOutOfRangeException ещё до показа формы.
        Shown += (_, _) => BeginInvoke(new Action(() => ApplyInitialSplitterLayout(splitContainer)));
    }

    private static void ApplyInitialSplitterLayout(SplitContainer splitContainer)
    {
        if (splitContainer.IsDisposed || splitContainer.ClientSize.Width <= splitContainer.SplitterWidth)
        {
            return;
        }

        int available = splitContainer.ClientSize.Width - splitContainer.SplitterWidth;
        int leftMinimum = ScaleLogicalPixels(LeftPanelMinimumLogical, splitContainer.DeviceDpi);
        int rightMinimum = ScaleLogicalPixels(RightPanelMinimumLogical, splitContainer.DeviceDpi);

        // Сначала возвращаем ограничения SplitContainer к безопасным значениям по
        // умолчанию, затем выставляем позицию разделителя и только после этого —
        // минимальные размеры. Такой порядок не зависит от временного Designer/handle size.
        splitContainer.Panel1MinSize = 0;
        splitContainer.Panel2MinSize = 0;

        int desired = (int)Math.Round(splitContainer.ClientSize.Width * DefaultLeftPanelRatio);

        if (leftMinimum + rightMinimum <= available)
        {
            int maximum = available - rightMinimum;
            desired = Math.Clamp(desired, leftMinimum, maximum);
            splitContainer.SplitterDistance = desired;
            splitContainer.Panel1MinSize = leftMinimum;
            splitContainer.Panel2MinSize = rightMinimum;
            return;
        }

        // На экстремально узкой временной области сохраняем рабочий UI без падения.
        // После показа штатный MinimumSize формы обычно делает эту ветку недостижимой.
        splitContainer.SplitterDistance = Math.Clamp(desired, 0, available);
    }

    private static int ScaleLogicalPixels(int logicalPixels, int deviceDpi) =>
        Math.Max(0, (int)Math.Round(logicalPixels * Math.Max(deviceDpi, 96) / 96d));

    private TableLayoutPanel BuildLeftPanel()
    {
        var actionButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        actionButtons.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        Button newButton = CreateButton("+ Новая", (_, _) => CreateEntry(), UiTheme.AccentTag);
        Button editButton = CreateButton("Изменить", (_, _) => EditSelectedEntry());
        Button historyButton = CreateButton("История", (_, _) => ShowPasswordHistory());
        Button deleteButton = CreateButton("Удалить", (_, _) => DeleteSelectedEntry(), UiTheme.DangerTag);

        foreach (Button button in new[] { newButton, editButton, historyButton, deleteButton })
        {
            button.AutoSize = false;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 8, 8);
        }
        editButton.Margin = new Padding(0, 0, 0, 8);
        deleteButton.Margin = new Padding(0);
        historyButton.Margin = new Padding(0, 0, 8, 0);

        actionButtons.Controls.Add(newButton, 0, 0);
        actionButtons.Controls.Add(editButton, 1, 0);
        actionButtons.Controls.Add(historyButton, 0, 1);
        actionButtons.Controls.Add(deleteButton, 1, 1);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20, 18, 18, 18),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = UiTheme.SurfaceAlt
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var brand = BuildBrandHeader();

        var headerLabel = new Label
        {
            Text = "Записи",
            AutoSize = true,
            Font = UiTheme.Font(11F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 16, 0, 7)
        };

        _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _searchBox.PlaceholderText = "Поиск по записям...";
        _searchBox.Margin = new Padding(0, 0, 0, 12);
        _entriesList.Dock = DockStyle.Fill;
        _entriesList.BorderStyle = BorderStyle.FixedSingle;
        _entriesList.IntegralHeight = false;

        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(headerLabel, 0, 1);
        layout.Controls.Add(_searchBox, 0, 2);
        layout.Controls.Add(_entriesList, 0, 3);
        layout.Controls.Add(actionButtons, 0, 4);
        return layout;
    }

    private Control BuildBrandHeader()
    {
        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = new PictureBox
        {
            Image = AppBrand.Icon.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(42, 42),
            Margin = new Padding(0, 0, 10, 0)
        };
        layout.SetRowSpan(logo, 2);

        var title = new Label
        {
            Text = AppBrand.Name,
            AutoSize = true,
            Font = UiTheme.Font(16F, FontStyle.Bold),
            ForeColor = UiTheme.Text,
            Margin = new Padding(0, 2, 0, 0)
        };
        var subtitle = new Label
        {
            Text = "Локальное зашифрованное хранилище",
            AutoSize = true,
            Font = UiTheme.Font(8.8F),
            ForeColor = UiTheme.Muted,
            Margin = new Padding(0, 0, 0, 0)
        };

        layout.Controls.Add(logo, 0, 0);
        layout.Controls.Add(title, 1, 0);
        layout.Controls.Add(subtitle, 1, 1);
        return layout;
    }

    private TableLayoutPanel BuildRightPanel()
    {
        _detailsTable.Dock = DockStyle.Top;
        _detailsTable.AutoSize = true;
        _detailsTable.ColumnCount = 3;
        _detailsTable.Padding = new Padding(0, 8, 8, 12);
        _detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        _detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _detailsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));

        var detailsScroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
        };
        detailsScroll.Controls.Add(_detailsTable);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 20, 22, 16),
            ColumnCount = 1,
            RowCount = 2
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(BuildHeaderPanel(), 0, 0);
        layout.Controls.Add(detailsScroll, 0, 1);
        return layout;
    }

    private TableLayoutPanel BuildHeaderPanel()
    {
        _titleLabel.AutoSize = true;
        _titleLabel.Font = UiTheme.Font(20F, FontStyle.Bold);
        _titleLabel.ForeColor = UiTheme.Text;
        _titleLabel.Text = "Выберите запись";
        _titleLabel.Margin = new Padding(0, 0, 0, 10);

        Button managementButton = CreateButton("Управление", (_, _) => OpenManagement());
        Button lockButton = CreateButton("Заблокировать", (_, _) => LockVault());
        managementButton.AutoSize = false;
        lockButton.AutoSize = false;
        managementButton.Size = new Size(132, UiTheme.ButtonHeight);
        lockButton.Size = new Size(146, UiTheme.ButtonHeight);

        var actionButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0)
        };
        actionButtons.Controls.Add(managementButton);
        actionButtons.Controls.Add(lockButton);

        _showSecretsBox.ForeColor = UiTheme.Text;
        _showSecretsBox.Margin = new Padding(0, 10, 12, 0);

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.Controls.Add(_showSecretsBox, 0, 0);
        actionRow.Controls.Add(actionButtons, 1, 0);

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            Margin = new Padding(0)
        };
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        headerLayout.Controls.Add(_titleLabel, 0, 0);
        headerLayout.Controls.Add(actionRow, 0, 1);
        return headerLayout;
    }

    private void WireEvents()
    {
        _entriesList.SelectedIndexChanged += (_, _) => RenderSelectedEntry();
        _entriesList.DoubleClick += (_, _) => EditSelectedEntry();
        _searchBox.TextChanged += (_, _) => RefreshEntries();
        KeyDown += MainFormOnKeyDown;
        _showSecretsBox.CheckedChanged += (_, _) => RenderSelectedEntry();
        _clipboardTimer.Tick += (_, _) => ClearClipboardIfUnchanged();
        _autoLockTimer.Tick += (_, _) => CheckAutoLock();
        Deactivate += (_, _) => _showSecretsBox.Checked = false;

        try
        {
            SystemEvents.SessionSwitch += SystemEventsOnSessionSwitch;
            SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
            _systemEventsSubscribed = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException)
        {
            // На редких серверных/ограниченных Windows-контекстах глобальные системные
            // события могут быть недоступны. Таймер бездействия продолжит работать.
            _systemEventsSubscribed = false;
        }

        FormClosing += (_, _) =>
        {
            _autoLockTimer.Stop();
            ClearClipboardIfUnchanged();
            ClearDetails();
        };
        FormClosed += (_, _) =>
        {
            if (_systemEventsSubscribed)
            {
                SystemEvents.SessionSwitch -= SystemEventsOnSessionSwitch;
                SystemEvents.PowerModeChanged -= SystemEventsOnPowerModeChanged;
            }
            _clipboardTimer.Dispose();
            _autoLockTimer.Dispose();
            _toolTip.Dispose();
        };
    }

    private void RefreshEntries(Guid? entryToSelect = null)
    {
        Guid? selectedId = entryToSelect ?? SelectedEntry?.Id;
        string query = _searchBox.Text.Trim();

        IEnumerable<VaultEntry> entries = _vault.Entries
            .Where(entry => MatchesSearch(entry, query))
            .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase);

        _entriesList.BeginUpdate();
        try
        {
            _entriesList.Items.Clear();
            foreach (VaultEntry entry in entries)
            {
                _entriesList.Items.Add(entry);
            }
        }
        finally
        {
            _entriesList.EndUpdate();
        }

        if (selectedId.HasValue)
        {
            VaultEntry? targetItem = _entriesList.Items
                .OfType<VaultEntry>()
                .FirstOrDefault(item => item.Id == selectedId.Value);

            if (targetItem is not null)
            {
                _entriesList.SelectedItem = targetItem;
            }
        }

        if (_entriesList.SelectedIndex < 0 && _entriesList.Items.Count > 0)
        {
            _entriesList.SelectedIndex = 0;
        }
        else if (_entriesList.Items.Count == 0)
        {
            RenderSelectedEntry();
        }
    }

    private static bool MatchesSearch(VaultEntry entry, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }

        string[] terms = query.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return terms.All(term =>
            entry.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || entry.Website.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || entry.UserName.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || entry.Notes.Contains(term, StringComparison.CurrentCultureIgnoreCase)
            || entry.CustomFields.Any(field =>
                field.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                field.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
    }

    private void MainFormOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.F)
        {
            _searchBox.Focus();
            _searchBox.SelectAll();
        }
        else if (e.Control && e.KeyCode == Keys.N)
        {
            CreateEntry();
        }
        else if (e.Control && e.KeyCode == Keys.L)
        {
            LockVault();
        }
        else if (e.KeyCode == Keys.Escape && _searchBox.TextLength > 0)
        {
            _searchBox.Clear();
            _searchBox.Focus();
        }
        else
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void RenderSelectedEntry()
    {
        _detailsTable.SuspendLayout();
        try
        {
            ClearDetails();

            VaultEntry? entry = SelectedEntry;
            if (entry is null)
            {
                _titleLabel.Text = _vault.EntryCount == 0 ? "Записей пока нет" : "Выберите запись";
                _showSecretsBox.Enabled = false;

                string message = _vault.EntryCount == 0
                    ? "Нажмите «Новая», чтобы создать первую запись."
                    : "По вашему запросу ничего не найдено.";

                AddMessageRow(message);
                return;
            }

            _titleLabel.Text = entry.Title;
            _showSecretsBox.Enabled = true;

            AddDetailRow("Сайт", entry.Website, isSecret: false);
            AddDetailRow("Логин", entry.UserName, isSecret: false);
            AddDetailRow("Пароль", entry.Password, isSecret: true);

            foreach (CustomField field in entry.CustomFields)
            {
                AddDetailRow(field.Name, field.Value, field.IsSecret, showWhenEmpty: true);
            }

            if (!string.IsNullOrWhiteSpace(entry.Notes))
            {
                AddNotesRow(entry.Notes);
            }

            AddUpdatedDateRow(entry.UpdatedUtc);
        }
        finally
        {
            _detailsTable.ResumeLayout(true);
        }
    }

    private void AddDetailRow(string name, string value, bool isSecret, bool showWhenEmpty = false)
    {
        if (string.IsNullOrEmpty(value) && !showWhenEmpty)
        {
            return;
        }

        int row = AddRowStyle(SizeType.AutoSize);
        var nameLabel = CreateLabel(name + ":", new Padding(3, 9, 8, 3), AnchorStyles.Left);

        string displayValue = value.Length == 0
            ? "—"
            : isSecret && !_showSecretsBox.Checked ? Mask(value) : value;

        var valueBox = new TextBox
        {
            Text = displayValue,
            ReadOnly = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(3, 5, 3, 3),
            ShortcutsEnabled = false,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };

        UiTheme.StyleTextBox(valueBox);

        var copyButton = CreateButton("⧉", (_, _) => CopyToClipboard(value, name));
        copyButton.AutoSize = false;
        copyButton.Size = new Size(40, UiTheme.ButtonHeight);
        copyButton.Padding = new Padding(0);
        copyButton.Margin = new Padding(6, 3, 3, 3);
        copyButton.Enabled = value.Length > 0;
        copyButton.AccessibleName = $"Копировать {name}";
        _toolTip.SetToolTip(copyButton, $"Копировать: {name}");

        _detailsTable.Controls.Add(nameLabel, 0, row);
        _detailsTable.Controls.Add(valueBox, 1, row);
        _detailsTable.Controls.Add(copyButton, 2, row);
    }

    private void AddNotesRow(string notes)
    {
        int labelRow = AddRowStyle(SizeType.AutoSize);
        var label = CreateLabel("Заметки:", new Padding(3, 14, 3, 3));
        _detailsTable.Controls.Add(label, 0, labelRow);
        _detailsTable.SetColumnSpan(label, 2);

        var copyButton = CreateButton("⧉", (_, _) => CopyToClipboard(notes, "Заметки"));
        copyButton.AutoSize = false;
        copyButton.Size = new Size(40, UiTheme.ButtonHeight);
        copyButton.Padding = new Padding(0);
        copyButton.Margin = new Padding(6, 8, 3, 3);
        copyButton.AccessibleName = "Копировать заметки";
        _toolTip.SetToolTip(copyButton, "Копировать: Заметки");
        _detailsTable.Controls.Add(copyButton, 2, labelRow);

        int notesRow = AddRowStyle(SizeType.AutoSize);
        var notesBox = new RichTextBox
        {
            Text = VaultEntryRules.NormalizeLineEndings(notes),
            ReadOnly = true,
            DetectUrls = false,
            WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, 110),
            ShortcutsEnabled = false,
            BackColor = UiTheme.Surface,
            ForeColor = UiTheme.Text,
            BorderStyle = BorderStyle.FixedSingle
        };
        _detailsTable.Controls.Add(notesBox, 0, notesRow);
        _detailsTable.SetColumnSpan(notesBox, 3);
    }

    private void AddMessageRow(string message)
    {
        int row = AddRowStyle(SizeType.AutoSize);
        var label = CreateLabel(message, new Padding(3, 18, 3, 3), foreColor: UiTheme.Muted);
        _detailsTable.Controls.Add(label, 0, row);
        _detailsTable.SetColumnSpan(label, 3);
    }

    private void AddUpdatedDateRow(DateTimeOffset updatedUtc)
    {
        int row = AddRowStyle(SizeType.AutoSize);
        var updatedLabel = CreateLabel(
            $"Изменено: {updatedUtc.ToLocalTime():g}",
            new Padding(3, 14, 3, 3),
            foreColor: UiTheme.Muted);
        _detailsTable.Controls.Add(updatedLabel, 1, row);
    }

    private void CreateEntry()
    {
        using var editor = new EntryEditorDialog();
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _vault.AddEntry(editor.ResultEntry);
            _searchBox.Clear();
            RefreshEntries(editor.ResultEntry.Id);
            SetStatus("Запись сохранена");
        }
        catch (Exception exception)
        {
            ShowSaveError(exception);
        }
    }

    private void EditSelectedEntry()
    {
        VaultEntry? selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        using var editor = new EntryEditorDialog(selected);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _vault.UpdateEntry(editor.ResultEntry);
            RefreshEntries(editor.ResultEntry.Id);
            SetStatus("Изменения сохранены");
        }
        catch (Exception exception)
        {
            ShowSaveError(exception);
        }
    }

    private void ShowPasswordHistory()
    {
        VaultEntry? selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        if (selected.PasswordHistory.Count == 0)
        {
            MessageBox.Show(
                "Для этой записи ещё нет сохранённых предыдущих паролей.",
                "История паролей",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var historyDialog = new PasswordHistoryDialog(
            selected,
            password => CopyToClipboard(password, "Старый пароль"));
        historyDialog.ShowDialog(this);
    }

    private void DeleteSelectedEntry()
    {
        VaultEntry? selected = SelectedEntry;
        if (selected is null)
        {
            return;
        }

        DialogResult confirmation = MessageBox.Show(
            $"Удалить запись «{selected.Title}»?",
            "Удаление записи",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _vault.DeleteEntry(selected.Id);
            RefreshEntries();
            SetStatus("Запись удалена");
        }
        catch (Exception exception)
        {
            ShowSaveError(exception);
        }
    }

    private void OpenManagement()
    {
        while (!IsDisposed && _vault.IsUnlocked)
        {
            using var dialog = new VaultManagementDialog(_vault);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            switch (dialog.RequestedAction)
            {
                case VaultManagementAction.ChangeMasterPassword:
                    ChangeMasterPassword();
                    break;
                case VaultManagementAction.Audit:
                    RunPasswordAudit();
                    return;
                case VaultManagementAction.Deduplicate:
                    DeduplicateEntries();
                    break;
                default:
                    return;
            }
        }
    }

    private void ChangeMasterPassword()
    {
        using var dialog = new ChangeMasterPasswordDialog(_vault);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SetStatus($"Мастер-пароль изменён • ревизия {_vault.Revision}");
        if (!dialog.BackupUpdated)
        {
            MessageBox.Show(
                "Мастер-пароль для основного vault.pnb успешно изменён, но не удалось обновить vault.pnb.bak. " +
                "Старая резервная копия может оставаться открываемой прежним мастер-паролем.\n\n" +
                "Проверьте права записи на папку и после этого измените любую запись, чтобы резервная копия обновилась.",
                "Резервная копия",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RunPasswordAudit()
    {
        PasswordAuditReport report = PasswordAuditor.Analyze(_vault.Entries);
        using var dialog = new PasswordAuditDialog(report);
        DialogResult result = dialog.ShowDialog(this);

        SetStatus(report.Findings.Count == 0
            ? "Аудит завершён: проблем не найдено"
            : $"Аудит завершён: найдено замечаний {report.Findings.Count}");

        if (result == DialogResult.OK && dialog.SelectedEntryId.HasValue)
        {
            _searchBox.Clear();
            RefreshEntries(dialog.SelectedEntryId.Value);
        }
    }

    private void DeduplicateEntries()
    {
        DialogResult confirmation = MessageBox.Show(
            "Удалить полностью идентичные дубли записей?\n\nЗаписи с отличающимися заметками или дополнительными полями удаляться не будут.",
            "Удаление дублей",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        try
        {
            int removed = _vault.Deduplicate();
            RefreshEntries();
            SetStatus(removed == 0 ? "Полных дублей не найдено" : $"Удалено дублей: {removed}");

            if (removed > 0)
            {
                MessageBox.Show(
                    $"Удалено полностью идентичных записей: {removed}.",
                    "Удаление дублей",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception exception)
        {
            ShowSaveError(exception);
        }
    }

    private void LockVault()
    {
        if (_isLocking || !_vault.IsUnlocked)
        {
            return;
        }

        _isLocking = true;
        try
        {
            PrepareLockedState();
            if (!PromptForUnlock())
            {
                Close();
            }
        }
        finally
        {
            _isLocking = false;
        }
    }

    private void LockVaultFromSystem(string reason)
    {
        if (_isLocking || !_vault.IsUnlocked)
        {
            return;
        }

        _isLocking = true;
        try
        {
            PrepareLockedState();
            _lockedBySystemEvent = true;
            SetStatus(reason);
        }
        finally
        {
            _isLocking = false;
        }
    }

    private void ResumeAfterSystemLock()
    {
        if (_isLocking || !_lockedBySystemEvent || _vault.IsUnlocked || IsDisposed || Disposing)
        {
            return;
        }

        _isLocking = true;
        _lockedBySystemEvent = false;
        try
        {
            if (!PromptForUnlock())
            {
                Close();
            }
        }
        finally
        {
            _isLocking = false;
        }
    }

    private void PrepareLockedState()
    {
        _autoLockTimer.Stop();
        _showSecretsBox.Checked = false;
        _entriesList.Items.Clear();
        ClearDetails();
        ClearClipboardIfUnchanged();
        _vault.Lock();
        Hide();
    }

    private bool PromptForUnlock()
    {
        using var unlockDialog = new UnlockDialog(_vault);
        if (unlockDialog.ShowDialog() != DialogResult.OK)
        {
            return false;
        }

        RefreshEntries();
        Show();
        Activate();
        _autoLockTimer.Start();
        SetStatus($"Хранилище разблокировано • ревизия {_vault.Revision}");
        return true;
    }

    private void CheckAutoLock()
    {
        if (!_vault.IsUnlocked || _isLocking || !Visible)
        {
            return;
        }

        if (TryGetSystemIdleTime(out TimeSpan idleTime) && idleTime >= AutoLockAfter)
        {
            SetStatus("Автоблокировка после 5 минут бездействия");
            LockVault();
        }
    }

    private void SystemEventsOnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.ConsoleDisconnect:
            case SessionSwitchReason.RemoteDisconnect:
                RunOnUiThread(() => LockVaultFromSystem("Хранилище заблокировано вместе с сеансом Windows"));
                break;

            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                RunOnUiThread(ResumeAfterSystemLock);
                break;
        }
    }

    private void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            RunOnUiThread(() => LockVaultFromSystem("Хранилище заблокировано перед переходом Windows в сон"));
        }
        else if (e.Mode == PowerModes.Resume)
        {
            RunOnUiThread(ResumeAfterSystemLock);
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Окно уже закрывается; системное событие можно безопасно проигнорировать.
        }
    }

    private void CopyToClipboard(string value, string fieldName)
    {
        if (value.Length == 0)
        {
            return;
        }

        try
        {
            SetSensitiveClipboardText(value);
            _clipboardSequence = GetClipboardSequenceNumber();
            _clipboardOwned = true;
            _clipboardTimer.Stop();
            _clipboardTimer.Start();
            SetStatus($"Поле «{fieldName}» скопировано; буфер очистится через 30 секунд");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"Не удалось скопировать значение.\n\n{exception.Message}",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static void SetSensitiveClipboardText(string value)
    {
        var data = new DataObject();
        data.SetText(value, TextDataFormat.UnicodeText);

        // Для этого зарегистрированного формата Windows учитывает сам факт его наличия:
        // элемент не должен попадать ни в историю буфера, ни в облачную синхронизацию.
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", false, "GeniaPassword");
        Clipboard.SetDataObject(data, true);
    }

    private void ClearClipboardIfUnchanged()
    {
        _clipboardTimer.Stop();
        if (!_clipboardOwned)
        {
            return;
        }

        try
        {
            if (GetClipboardSequenceNumber() == _clipboardSequence)
            {
                Clipboard.Clear();
                SetStatus("Буфер обмена очищен");
            }
        }
        catch
        {
            // Буфер может быть временно занят другим приложением.
        }
        finally
        {
            _clipboardOwned = false;
            _clipboardSequence = 0;
        }
    }

    private void ClearDetails()
    {
        Control[] controls = _detailsTable.Controls.Cast<Control>().ToArray();
        _detailsTable.Controls.Clear();
        foreach (Control control in controls)
        {
            control.Dispose();
        }

        _detailsTable.RowStyles.Clear();
        _detailsTable.RowCount = 0;
    }

    private void SetStatus(string text) => _statusLabel.Text = text;

    private static string Mask(string value) => new('•', Math.Clamp(value.Length, 8, 24));

    private int AddRowStyle(SizeType sizeType)
    {
        int row = _detailsTable.RowCount++;
        _detailsTable.RowStyles.Add(new RowStyle(sizeType));
        return row;
    }

    private static Button CreateButton(string text, EventHandler onClick, string? kind = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Tag = kind
        };
        button.Click += onClick;
        UiTheme.StyleButton(button);
        return button;
    }

    private static Label CreateLabel(
        string text,
        Padding margin,
        AnchorStyles anchor = AnchorStyles.Top | AnchorStyles.Left,
        Color? foreColor = null)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = anchor,
            Margin = margin
        };

        label.ForeColor = foreColor ?? UiTheme.Text;
        return label;
    }

    private void ShowSaveError(Exception exception)
    {
        if (exception is VaultExternalChangeException)
        {
            MessageBox.Show(
                exception.Message,
                "Обнаружено внешнее изменение vault",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        MessageBox.Show(
            $"Не удалось сохранить изменения. Проверьте данные, доступ к папке программы и свободное место.\n\n{exception.Message}",
            Text,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static bool TryGetSystemIdleTime(out TimeSpan idleTime)
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            idleTime = TimeSpan.Zero;
            return false;
        }

        uint currentTick = unchecked((uint)Environment.TickCount);
        uint elapsedMilliseconds = unchecked(currentTick - info.Time);
        idleTime = TimeSpan.FromMilliseconds(elapsedMilliseconds);
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);
}
