using System.Runtime.InteropServices;

namespace GeniaPassword.Security;

internal static class SecureInput
{
    internal static void ConfigurePasswordBox(TextBox box, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(box);
        box.MaxLength = maxLength;
        box.ShortcutsEnabled = false;
        box.KeyDown += (_, e) =>
        {
            bool paste = (e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert);
            if (!paste)
            {
                return;
            }

            TryPaste(box);
            e.SuppressKeyPress = true;
            e.Handled = true;
        };
    }

    private static void TryPaste(TextBox box)
    {
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText) && !Clipboard.ContainsText())
            {
                return;
            }

            string value = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (value.Length == 0)
            {
                value = Clipboard.GetText();
            }

            value = value.Replace("\r", string.Empty, StringComparison.Ordinal)
                         .Replace("\n", string.Empty, StringComparison.Ordinal);

            int remaining = box.MaxLength - (box.TextLength - box.SelectionLength);
            if (remaining > 0)
            {
                box.SelectedText = value[..Math.Min(value.Length, remaining)];
            }
        }
        catch (ExternalException)
        {
            // Буфер временно занят другим приложением.
        }
    }
}
