using System.Reflection;

namespace GeniaPassword;

internal static class AppBrand
{
    internal const string Name = "GeniaPassword";

    private static readonly Lazy<Icon> SharedIcon = new(LoadIcon);

    internal static Icon Icon => SharedIcon.Value;

    private static Icon LoadIcon()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GeniaPassword.ico")
            ?? throw new InvalidOperationException("Не удалось загрузить иконку приложения.");
        using var sourceIcon = new Icon(stream);
        return (Icon)sourceIcon.Clone();
    }
}
