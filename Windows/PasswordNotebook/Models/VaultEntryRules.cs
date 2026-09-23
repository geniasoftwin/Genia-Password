namespace GeniaPassword.Models;

internal static class VaultEntryRules
{
    internal const int MaxEntries = 10_000;
    internal const int MaxTitleLength = 200;
    internal const int MaxWebsiteLength = 2_048;
    internal const int MaxUserNameLength = 512;
    internal const int MaxPasswordLength = 4_096;
    internal const int MaxPasswordHistory = 5;
    internal const int MaxNotesLength = 100_000;
    internal const int MaxCustomFields = 100;
    internal const int MaxCustomFieldNameLength = 100;
    internal const int MaxCustomFieldValueLength = 100_000;

    private static readonly HashSet<string> ReservedFieldNames = new(StringComparer.CurrentCultureIgnoreCase)
    {
        "Название", "Сайт", "Логин", "Пароль", "Заметки",
        "Title", "Website", "Login", "Username", "UserName", "Password", "Notes"
    };

    internal static VaultEntry NormalizeAndValidate(VaultEntry source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string title = (source.Title ?? string.Empty).Trim();
        string website = NormalizeSingleLine(source.Website, trim: true);
        string userName = NormalizeSingleLine(source.UserName, trim: true);
        string password = source.Password ?? string.Empty;
        string notes = NormalizeLineEndings(source.Notes ?? string.Empty);
        List<CustomField> sourceFields = source.CustomFields ?? [];
        List<PasswordHistoryItem> sourceHistory = source.PasswordHistory ?? [];

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Введите название записи.", nameof(source));
        }

        EnsureLength(title, MaxTitleLength, "Название");
        EnsureLength(website, MaxWebsiteLength, "Сайт");
        EnsureLength(userName, MaxUserNameLength, "Логин");
        EnsureLength(password, MaxPasswordLength, "Пароль");
        EnsureLength(notes, MaxNotesLength, "Заметки");

        if (sourceFields.Count > MaxCustomFields)
        {
            throw new ArgumentException($"Допускается не более {MaxCustomFields} дополнительных полей.", nameof(source));
        }

        var passwordHistory = new List<PasswordHistoryItem>(Math.Min(sourceHistory.Count, MaxPasswordHistory));
        foreach (PasswordHistoryItem? historyItem in sourceHistory.Take(MaxPasswordHistory))
        {
            if (historyItem is null)
            {
                continue;
            }

            string historicalPassword = historyItem.Password ?? string.Empty;
            if (historicalPassword.Length == 0)
            {
                continue;
            }

            EnsureLength(historicalPassword, MaxPasswordLength, "Пароль в истории");
            passwordHistory.Add(new PasswordHistoryItem
            {
                Password = historicalPassword,
                ActiveSinceUtc = historyItem.ActiveSinceUtc,
                ReplacedUtc = historyItem.ReplacedUtc
            });
        }

        var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var customFields = new List<CustomField>(sourceFields.Count);

        foreach (CustomField? field in sourceFields)
        {
            if (field is null)
            {
                continue;
            }

            string name = NormalizeSingleLine(field.Name, trim: true);
            string value = NormalizeSingleLine(field.Value, trim: false);

            if (name.Length == 0 && value.Length == 0)
            {
                continue;
            }

            if (name.Length == 0)
            {
                throw new ArgumentException("Укажите название для каждого дополнительного поля.", nameof(source));
            }

            EnsureLength(name, MaxCustomFieldNameLength, "Название дополнительного поля");
            EnsureLength(value, MaxCustomFieldValueLength, $"Значение поля «{name}»");

            if (ReservedFieldNames.Contains(name))
            {
                throw new ArgumentException($"Название «{name}» зарезервировано для стандартного поля.", nameof(source));
            }

            if (!names.Add(name))
            {
                throw new ArgumentException($"Дополнительное поле «{name}» указано несколько раз.", nameof(source));
            }

            customFields.Add(new CustomField
            {
                Name = name,
                Value = value,
                IsSecret = field.IsSecret
            });
        }

        return new VaultEntry
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            Title = title,
            Website = website,
            UserName = userName,
            Password = password,
            Notes = notes,
            CustomFields = customFields,
            UpdatedUtc = source.UpdatedUtc,
            PasswordUpdatedUtc = source.PasswordUpdatedUtc,
            PasswordHistory = passwordHistory
        };
    }

    internal static bool IsReservedCustomFieldName(string name) =>
        ReservedFieldNames.Contains(name.Trim());

    internal static string NormalizeLineEndings(string? value) =>
        (value ?? string.Empty).ReplaceLineEndings(Environment.NewLine);

    private static string NormalizeSingleLine(string? value, bool trim)
    {
        string normalized = (value ?? string.Empty)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        return trim ? normalized.Trim() : normalized;
    }

    private static void EnsureLength(string value, int maximumLength, string fieldName)
    {
        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"Поле «{fieldName}» слишком длинное. Максимум: {maximumLength:N0} символов.");
        }
    }
}
