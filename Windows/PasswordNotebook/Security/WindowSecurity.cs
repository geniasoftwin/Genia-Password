using System.Runtime.InteropServices;

namespace GeniaPassword.Security;

internal static class WindowSecurity
{
    private const uint WdaMonitor = 0x00000001;
    private const uint WdaExcludeFromCapture = 0x00000011;

    internal static void Protect(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.HandleCreated += (_, _) => ApplyDisplayAffinity(form);
        if (form.IsHandleCreated)
        {
            ApplyDisplayAffinity(form);
        }
    }

    private static void ApplyDisplayAffinity(Form form)
    {
        try
        {
            if (!SetWindowDisplayAffinity(form.Handle, WdaExcludeFromCapture))
            {
                _ = SetWindowDisplayAffinity(form.Handle, WdaMonitor);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
