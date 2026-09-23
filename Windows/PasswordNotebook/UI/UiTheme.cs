namespace GeniaPassword.UI;

internal static class UiTheme
{
    // Classic WinForms palette, close to the GeniaPassword 2.0 appearance:
    // grey application chrome, standard Windows buttons and white input areas.
    internal static readonly Color Window = SystemColors.Control;
    internal static readonly Color Surface = SystemColors.Window;
    internal static readonly Color SurfaceAlt = SystemColors.Control;
    internal static readonly Color Accent = SystemColors.Control;
    internal static readonly Color AccentHover = SystemColors.ControlLight;
    internal static readonly Color Text = SystemColors.ControlText;
    internal static readonly Color Muted = SystemColors.GrayText;
    internal static readonly Color Border = SystemColors.ControlDark;
    internal static readonly Color Danger = SystemColors.ControlText;
    internal static readonly Color Success = SystemColors.ControlText;

    // Tags are kept for source compatibility, but the classic theme intentionally
    // renders every action as a normal Windows button (no blue/red custom fills).
    internal const string AccentTag = "ui-accent";
    internal const string DangerTag = "ui-danger";
    internal const string GhostTag = "ui-ghost";

    internal const int ButtonHeight = 32;

    internal static Font Font(float size = 9F, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);

    internal static void Apply(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = Text;
        StyleTree(form);
    }

    internal static void StyleTree(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case Button button:
                    StyleButton(button);
                    break;

                case TextBox textBox:
                    StyleTextBox(textBox);
                    break;

                case RichTextBox richTextBox:
                    richTextBox.BackColor = SystemColors.Window;
                    richTextBox.ForeColor = SystemColors.WindowText;
                    richTextBox.BorderStyle = BorderStyle.Fixed3D;
                    break;

                case ListBox listBox:
                    listBox.BackColor = SystemColors.Window;
                    listBox.ForeColor = SystemColors.WindowText;
                    listBox.BorderStyle = BorderStyle.Fixed3D;
                    break;

                case DataGridView grid:
                    StyleGrid(grid);
                    break;

                case CheckBox checkBox:
                    checkBox.ForeColor = SystemColors.ControlText;
                    break;

                case Label label when label.ForeColor == SystemColors.ControlText:
                    label.ForeColor = SystemColors.ControlText;
                    break;

                case StatusStrip strip:
                    strip.BackColor = SystemColors.Control;
                    strip.ForeColor = SystemColors.ControlText;
                    strip.SizingGrip = false;
                    break;
            }

            StyleTree(control);
        }
    }

    internal static void StyleButton(Button button)
    {
        // Use the native Windows button renderer. Besides restoring the grey 2.0
        // look, it avoids text clipping introduced by the custom flat-button
        // padding used in the earlier 2.1 UI.
        button.FlatStyle = FlatStyle.System;
        button.BackColor = SystemColors.Control;
        button.ForeColor = SystemColors.ControlText;
        button.UseVisualStyleBackColor = true;
        button.Cursor = Cursors.Default;
        button.Padding = Padding.Empty;
        button.AutoEllipsis = false;
        button.UseCompatibleTextRendering = false;

        // Keep a comfortable classic height, but never force a too-small width.
        int textWidth = TextRenderer.MeasureText(
            button.Text ?? string.Empty,
            button.Font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        int minimumWidth = Math.Max(0, textWidth + 24);

        if (button.Dock == DockStyle.Fill)
        {
            button.MinimumSize = new Size(0, ButtonHeight);
        }
        else
        {
            button.MinimumSize = new Size(minimumWidth, ButtonHeight);
            if (!button.AutoSize)
            {
                button.Width = Math.Max(button.Width, minimumWidth);
                button.Height = Math.Max(button.Height, ButtonHeight);
            }
        }
    }

    internal static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = SystemColors.Window;
        textBox.ForeColor = SystemColors.WindowText;
        textBox.BorderStyle = BorderStyle.Fixed3D;

        // Standard single-line WinForms EDIT: native preferred height gives normal
        // vertical centering of text and caret at every DPI. No manual Y-offsets.
        if (!textBox.Multiline)
        {
            textBox.AutoSize = true;
            textBox.TextAlign = HorizontalAlignment.Left;
        }
    }

    internal static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = SystemColors.AppWorkspace;
        grid.BorderStyle = BorderStyle.Fixed3D;
        grid.GridColor = SystemColors.ControlDark;
        grid.EnableHeadersVisualStyles = true;
        grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SystemColors.Control;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
        grid.DefaultCellStyle.BackColor = SystemColors.Window;
        grid.DefaultCellStyle.ForeColor = SystemColors.WindowText;
        grid.DefaultCellStyle.SelectionBackColor = SystemColors.Highlight;
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.HighlightText;
        grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        grid.RowTemplate.Height = 30;
    }
}
