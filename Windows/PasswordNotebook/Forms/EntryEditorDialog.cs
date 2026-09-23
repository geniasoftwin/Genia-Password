using System.Runtime.InteropServices;
using GeniaPassword.Models;
using GeniaPassword.Security;
using GeniaPassword.UI;

namespace GeniaPassword.Forms;

internal sealed class EntryEditorDialog(VaultEntry? entry = null) : Form
{
    private readonly TextBox _titleBox = new() { MaxLength = VaultEntryRules.MaxTitleLength };
    private readonly TextBox _websiteBox = new() { MaxLength = VaultEntryRules.MaxWebsiteLength, ShortcutsEnabled = true };
    private readonly TextBox _userNameBox = new() { MaxLength = VaultEntryRules.MaxUserNameLength };
    private readonly TextBox _passwordBox = new() { UseSystemPasswordChar = true, MaxLength = VaultEntryRules.MaxPasswordLength, ShortcutsEnabled = false };
    private readonly RichTextBox _notesBox = new() { MaxLength = VaultEntryRules.MaxNotesLength };
    private readonly DataGridView _fieldsGrid = new();
    private readonly VaultEntry _workingEntry = entry?.Clone() ?? new VaultEntry();
    private readonly Button _showPasswordButton = new() { Text = "Показать", AutoSize = true };

    internal VaultEntry ResultEntry { get; private set; } = entry?.Clone() ?? new VaultEntry();

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ConfigureForm(entry is null);
        ConfigureFieldsGrid();
        BuildLayout();
        UiTheme.Apply(this);
        WindowSecurity.Protect(this);
        LoadEntry();
        WireInputEvents();
    }

    private void ConfigureForm(bool isNewEntry)
    {
        Text = isNewEntry ? "Новая запись" : "Изменение записи";
        Icon = AppBrand.Icon;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(720, 700);
        Size = new Size(820, 780);
        Font = UiTheme.Font(10F);
    }

    private void ConfigureFieldsGrid()
    {
        _fieldsGrid.Dock = DockStyle.Fill;
        _fieldsGrid.AllowUserToAddRows = false;
        _fieldsGrid.AllowUserToDeleteRows = false;
        _fieldsGrid.AutoGenerateColumns = false;
        _fieldsGrid.RowHeadersVisible = false;
        _fieldsGrid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _fieldsGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _fieldsGrid.MultiSelect = false;
        _fieldsGrid.BackgroundColor = SystemColors.Window;
        _fieldsGrid.BorderStyle = BorderStyle.Fixed3D;
        _fieldsGrid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;

        _fieldsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FieldName",
            HeaderText = "Название поля",
            MaxInputLength = VaultEntryRules.MaxCustomFieldNameLength,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 40
        });

        _fieldsGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "FieldValue",
            HeaderText = "Значение",
            MaxInputLength = VaultEntryRules.MaxCustomFieldValueLength,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 50
        });

        _fieldsGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "FieldSecret",
            HeaderText = "Секрет",
            Width = 75
        });
    }

    private void BuildLayout()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 8
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        mainLayout.Controls.Add(BuildStandardFieldsPanel(), 0, 0);
        mainLayout.Controls.Add(CreateHeaderLabel("Дополнительные поля:"), 0, 1);
        mainLayout.Controls.Add(_fieldsGrid, 0, 2);
        mainLayout.Controls.Add(BuildFieldsActionButtons(), 0, 3);
        mainLayout.Controls.Add(CreateHeaderLabel("Заметки:"), 0, 4);

        _notesBox.Dock = DockStyle.Fill;
        _notesBox.AcceptsTab = true;
        _notesBox.WordWrap = true;
        _notesBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        mainLayout.Controls.Add(_notesBox, 0, 5);

        mainLayout.Controls.Add(new Label
        {
            Text = "Отметьте «Секрет», чтобы скрывать значение дополнительного поля при просмотре.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 8)
        }, 0, 6);

        mainLayout.Controls.Add(BuildDialogButtons(), 0, 7);
        Controls.Add(mainLayout);
    }

    private TableLayoutPanel BuildStandardFieldsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddLabeledField(panel, "Название:*", _titleBox, 0);
        AddLabeledField(panel, "Сайт:", _websiteBox, 1);
        AddLabeledField(panel, "Логин:", _userNameBox, 2);

        panel.Controls.Add(new Label
        {
            Text = "Пароль:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 14, 0)
        }, 0, 3);

        var passwordPanel = BuildPasswordPanel();
        passwordPanel.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(passwordPanel, 1, 3);
        return panel;
    }

    private TableLayoutPanel BuildPasswordPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true,
            Margin = new Padding(0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 136));

        var pastePasswordButton = new Button
        {
            Text = "Вставить",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0)
        };
        pastePasswordButton.Click += (_, _) => PastePasswordFromClipboard();

        _showPasswordButton.AutoSize = false;
        _showPasswordButton.Dock = DockStyle.Fill;
        _showPasswordButton.Margin = new Padding(8, 0, 0, 0);
        _showPasswordButton.Click += (_, _) => TogglePasswordVisibility();

        var generatePasswordButton = new Button
        {
            Text = "Генератор…",
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 0, 0, 0)
        };
        generatePasswordButton.Click += (_, _) => GeneratePasswordWithConfirmation();

        _passwordBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _passwordBox.Margin = new Padding(0);
        panel.Controls.Add(_passwordBox, 0, 0);
        panel.Controls.Add(pastePasswordButton, 1, 0);
        panel.Controls.Add(_showPasswordButton, 2, 0);
        panel.Controls.Add(generatePasswordButton, 3, 0);
        return panel;
    }

    private FlowLayoutPanel BuildFieldsActionButtons()
    {
        var addFieldButton = new Button { Text = "+ Добавить поле", AutoSize = true };
        var removeFieldButton = new Button { Text = "Удалить поле", AutoSize = true };

        addFieldButton.Click += (_, _) => AddCustomFieldRow();
        removeFieldButton.Click += (_, _) => TryRemoveSelectedField();

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 6, 0, 2)
        };
        panel.Controls.Add(addFieldButton);
        panel.Controls.Add(removeFieldButton);
        return panel;
    }

    private FlowLayoutPanel BuildDialogButtons()
    {
        var saveButton = new Button
        {
            Text = "Сохранить",
            AutoSize = false,
            Size = new Size(126, UiTheme.ButtonHeight),
            Tag = UiTheme.AccentTag
        };
        var cancelButton = new Button
        {
            Text = "Отмена",
            AutoSize = false,
            Size = new Size(112, UiTheme.ButtonHeight),
            DialogResult = DialogResult.Cancel
        };
        saveButton.Click += SaveEntry;

        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(saveButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        return panel;
    }

    private void WireInputEvents()
    {
        // Явная обработка вставки устраняет зависимость от поведения стандартного контекстного меню TextBox.
        _websiteBox.KeyDown += WebsiteBoxOnKeyDown;
        _passwordBox.KeyDown += PasswordBoxOnKeyDown;
        _fieldsGrid.KeyDown += FieldsGridOnKeyDown;
    }

    private static void AddLabeledField(TableLayoutPanel panel, string labelText, Control field, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 14, 0)
        }, 0, row);

        if (field is TextBox)
        {
            field.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }
        else
        {
            field.Dock = DockStyle.Fill;
        }
        field.Margin = new Padding(0, 4, 0, 4);
        panel.Controls.Add(field, 1, row);
    }

    private static Label CreateHeaderLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 8, 0, 4),
        Font = UiTheme.Font(10F, FontStyle.Bold)
    };

    private void LoadEntry()
    {
        _titleBox.Text = _workingEntry.Title;
        _websiteBox.Text = _workingEntry.Website;
        _userNameBox.Text = _workingEntry.UserName;
        _passwordBox.Text = _workingEntry.Password;
        _notesBox.Text = VaultEntryRules.NormalizeLineEndings(_workingEntry.Notes);

        foreach (CustomField field in _workingEntry.CustomFields)
        {
            _fieldsGrid.Rows.Add(field.Name, field.Value, field.IsSecret);
        }
    }

    private void TogglePasswordVisibility()
    {
        bool show = _passwordBox.UseSystemPasswordChar;
        _passwordBox.UseSystemPasswordChar = !show;
        _showPasswordButton.Text = show ? "Скрыть" : "Показать";
        _passwordBox.Focus();
    }

    private void GeneratePasswordWithConfirmation()
    {
        using var generator = new PasswordGeneratorDialog();
        if (generator.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (_passwordBox.TextLength > 0)
        {
            DialogResult confirmation = MessageBox.Show(
                "В поле уже находится пароль. Заменить его новым случайным паролем?\n\n" +
                "Сохранённый ранее пароль существующей записи будет доступен в истории паролей после сохранения.",
                "Замена пароля",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        _passwordBox.Text = generator.GeneratedPassword;
        _passwordBox.UseSystemPasswordChar = true;
        _showPasswordButton.Text = "Показать";
        _passwordBox.SelectAll();
        _passwordBox.Focus();
    }

    private void AddCustomFieldRow()
    {
        if (_fieldsGrid.Rows.Count >= VaultEntryRules.MaxCustomFields)
        {
            MessageBox.Show(
                $"Допускается не более {VaultEntryRules.MaxCustomFields} дополнительных полей.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        int rowIndex = _fieldsGrid.Rows.Add("", "", false);
        _fieldsGrid.CurrentCell = _fieldsGrid.Rows[rowIndex].Cells["FieldName"];
        _fieldsGrid.BeginEdit(true);
    }

    private void TryRemoveSelectedField()
    {
        if (_fieldsGrid.CurrentRow is null)
        {
            return;
        }

        string fieldName = Convert.ToString(_fieldsGrid.CurrentRow.Cells["FieldName"].Value)?.Trim() ?? string.Empty;
        string fieldValue = Convert.ToString(_fieldsGrid.CurrentRow.Cells["FieldValue"].Value) ?? string.Empty;

        if (fieldName.Length > 0 || fieldValue.Length > 0)
        {
            DialogResult confirm = MessageBox.Show(
                fieldName.Length > 0 ? $"Удалить поле «{fieldName}»?" : "Удалить выбранное поле?",
                "Подтверждение удаления",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        _fieldsGrid.Rows.Remove(_fieldsGrid.CurrentRow);
    }

    private void PastePasswordFromClipboard()
    {
        if (!TryReadClipboardText(out string text))
        {
            MessageBox.Show(
                "Буфер обмена не содержит текста.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _passwordBox.Focus();
            return;
        }

        string singleLine = ToSingleLine(text);
        if (singleLine.Length == 0)
        {
            _passwordBox.Focus();
            return;
        }

        if (singleLine.Length > _passwordBox.MaxLength)
        {
            MessageBox.Show(
                $"Пароль из буфера обмена длиннее максимально допустимых {_passwordBox.MaxLength} символов.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            _passwordBox.Focus();
            return;
        }

        if (_passwordBox.TextLength > 0 && !string.Equals(_passwordBox.Text, singleLine, StringComparison.Ordinal))
        {
            DialogResult confirmation = MessageBox.Show(
                "В поле уже находится пароль. Заменить его паролем из буфера обмена?",
                "Замена пароля",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirmation != DialogResult.Yes)
            {
                _passwordBox.Focus();
                return;
            }
        }

        _passwordBox.Text = singleLine;
        _passwordBox.UseSystemPasswordChar = true;
        _showPasswordButton.Text = "Показать";
        _passwordBox.SelectAll();
        _passwordBox.Focus();
    }

    private void PasswordBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.V)
        {
            return;
        }

        if (TryReadClipboardText(out string text))
        {
            string singleLine = ToSingleLine(text);
            int remaining = _passwordBox.MaxLength - (_passwordBox.TextLength - _passwordBox.SelectionLength);
            if (remaining > 0)
            {
                _passwordBox.SelectedText = singleLine[..Math.Min(singleLine.Length, remaining)];
            }
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void WebsiteBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.V)
        {
            return;
        }

        if (!TryReadClipboardText(out string text))
        {
            return;
        }

        string singleLine = ToSingleLine(text);
        int remaining = _websiteBox.MaxLength - (_websiteBox.TextLength - _websiteBox.SelectionLength);
        if (remaining > 0)
        {
            _websiteBox.SelectedText = singleLine[..Math.Min(singleLine.Length, remaining)];
        }

        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void FieldsGridOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.Control || e.KeyCode != Keys.V || _fieldsGrid.CurrentCell is null)
        {
            return;
        }

        if (!TryReadClipboardText(out string text))
        {
            return;
        }

        PasteIntoFieldsGrid(text);
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void PasteIntoFieldsGrid(string clipboardText)
    {
        DataGridViewCell startCell = _fieldsGrid.CurrentCell!;
        if (startCell.ColumnIndex > 1)
        {
            return;
        }

        string[] lines = clipboardText.ReplaceLineEndings("\n").Split('\n');
        int targetRow = startCell.RowIndex;

        foreach (string rawLine in lines)
        {
            if (rawLine.Length == 0 && lines.Length > 1)
            {
                continue;
            }

            if (targetRow >= VaultEntryRules.MaxCustomFields)
            {
                break;
            }

            while (targetRow >= _fieldsGrid.Rows.Count)
            {
                _fieldsGrid.Rows.Add("", "", false);
            }

            string[] cells = rawLine.Split('\t');
            for (int offset = 0; offset < cells.Length; offset++)
            {
                int column = startCell.ColumnIndex + offset;
                if (column > 1)
                {
                    break;
                }

                int maxLength = column == 0
                    ? VaultEntryRules.MaxCustomFieldNameLength
                    : VaultEntryRules.MaxCustomFieldValueLength;
                string value = ToSingleLine(cells[offset]);
                _fieldsGrid.Rows[targetRow].Cells[column].Value = value[..Math.Min(value.Length, maxLength)];
            }

            targetRow++;
        }
    }

    private static bool TryReadClipboardText(out string text)
    {
        text = string.Empty;
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText) && !Clipboard.ContainsText())
            {
                return false;
            }

            text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (text.Length == 0)
            {
                text = Clipboard.GetText();
            }
            return text.Length > 0;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static string ToSingleLine(string value) =>
        value.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');

    private void SaveEntry(object? sender, EventArgs e)
    {
        _fieldsGrid.EndEdit();

        if (string.IsNullOrWhiteSpace(_titleBox.Text))
        {
            MessageBox.Show("Введите название записи.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _titleBox.Focus();
            return;
        }

        var customFields = new List<CustomField>();
        var fieldNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);

        foreach (DataGridViewRow row in _fieldsGrid.Rows)
        {
            string name = Convert.ToString(row.Cells["FieldName"].Value)?.Trim() ?? string.Empty;
            string value = Convert.ToString(row.Cells["FieldValue"].Value) ?? string.Empty;
            bool isSecret = Convert.ToBoolean(row.Cells["FieldSecret"].Value ?? false);

            if (name.Length == 0 && value.Length == 0)
            {
                continue;
            }

            if (name.Length == 0)
            {
                ShowFieldValidationError(row, "Укажите название для каждого дополнительного поля.");
                return;
            }

            if (VaultEntryRules.IsReservedCustomFieldName(name))
            {
                ShowFieldValidationError(row, $"Название «{name}» зарезервировано для стандартного поля.");
                return;
            }

            if (!fieldNames.Add(name))
            {
                ShowFieldValidationError(row, $"Дополнительное поле «{name}» указано несколько раз.");
                return;
            }

            customFields.Add(new CustomField { Name = name, Value = value, IsSecret = isSecret });
        }

        try
        {
            ResultEntry = VaultEntryRules.NormalizeAndValidate(new VaultEntry
            {
                Id = _workingEntry.Id,
                Title = _titleBox.Text,
                Website = _websiteBox.Text,
                UserName = _userNameBox.Text,
                Password = _passwordBox.Text,
                Notes = _notesBox.Text,
                CustomFields = customFields,
                UpdatedUtc = DateTimeOffset.UtcNow,
                PasswordUpdatedUtc = _workingEntry.PasswordUpdatedUtc,
                PasswordHistory = [.. _workingEntry.PasswordHistory.Select(item => item.Clone())]
            });

            DialogResult = DialogResult.OK;
        }
        catch (ArgumentException exception)
        {
            MessageBox.Show(exception.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowFieldValidationError(DataGridViewRow row, string message)
    {
        MessageBox.Show(message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _fieldsGrid.CurrentCell = row.Cells["FieldName"];
        _fieldsGrid.BeginEdit(true);
    }
}
