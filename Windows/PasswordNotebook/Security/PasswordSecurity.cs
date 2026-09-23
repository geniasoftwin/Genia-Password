using GeniaPassword.Models;

namespace GeniaPassword.Security;

internal enum PasswordStrengthLevel
{
    TooShort,
    Weak,
    Good,
    Strong,
    Excellent
}

internal static class MasterPasswordPolicy
{
    internal const int MinimumLength = 8;
    internal const int MaximumLength = 1_024;

    /// <summary>
    /// Проверка для UI: ожидаемые ошибки ввода возвращаются обычным текстом и не
    /// раскрывают пользователю имена внутренних параметров или типы исключений.
    /// </summary>
    internal static bool TryValidateForCreation(string? password, out string errorMessage)
    {
        if (string.IsNullOrEmpty(password))
        {
            errorMessage = "Введите мастер-пароль.";
            return false;
        }

        if (password.Length < MinimumLength)
        {
            errorMessage = $"Мастер-пароль должен содержать не менее {MinimumLength} символов.";
            return false;
        }

        if (password.Length > MaximumLength)
        {
            errorMessage = $"Мастер-пароль слишком длинный. Максимум: {MaximumLength:N0} символов.";
            return false;
        }

        if (PasswordSecurity.IsCommonPassword(password))
        {
            errorMessage = "Этот мастер-пароль слишком распространён. Используйте длинную уникальную парольную фразу.";
            return false;
        }

        if (PasswordSecurity.IsSingleRepeatedCharacter(password))
        {
            errorMessage = "Мастер-пароль не должен состоять из одного повторяющегося символа.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    /// <summary>
    /// Защитная проверка сервисного слоя. UI должен использовать TryValidateForCreation,
    /// но сервис всё равно не доверяет вызывающему коду.
    /// </summary>
    internal static void ValidateForCreation(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryValidateForCreation(password, out string errorMessage))
        {
            // Намеренно без имени параметра: даже если это исключение когда-либо
            // попадёт в пользовательское окно, технического "(Parameter 'password')" не будет.
            throw new ArgumentException(errorMessage);
        }
    }
}

internal static class PasswordSecurity
{
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456", "12345678", "123456789", "1234567890", "qwerty", "qwerty123",
        "password", "password1", "admin", "administrator", "letmein", "welcome",
        "iloveyou", "monkey", "dragon", "master", "login", "abc123", "111111",
        "000000", "1q2w3e4r", "qwertyuiop", "пароль", "пароль123", "йцукен",
        "123456789012345", "qwertyuiopasdfg", "passwordpassword"
    };

    internal static PasswordStrengthLevel EvaluateStrength(string password)
    {
        if (password.Length < MasterPasswordPolicy.MinimumLength)
        {
            return PasswordStrengthLevel.TooShort;
        }

        if (IsCommonPassword(password) || IsSingleRepeatedCharacter(password))
        {
            return PasswordStrengthLevel.Weak;
        }

        // Не навязываем правила композиции: длинная парольная фраза из слов и пробелов
        // может быть сильнее короткого "сложного" пароля. Разнообразие символов лишь
        // немного повышает оценку, но не является обязательным условием.
        int diversity = 0;
        if (password.Any(char.IsLower)) diversity++;
        if (password.Any(char.IsUpper)) diversity++;
        if (password.Any(char.IsDigit)) diversity++;
        if (password.Any(ch => !char.IsLetterOrDigit(ch))) diversity++;

        if (password.Length >= 28)
        {
            return PasswordStrengthLevel.Excellent;
        }

        if (password.Length >= 20 || (password.Length >= 18 && diversity >= 3))
        {
            return PasswordStrengthLevel.Strong;
        }

        return diversity >= 2 ? PasswordStrengthLevel.Good : PasswordStrengthLevel.Weak;
    }

    internal static bool IsCommonPassword(string password) => CommonPasswords.Contains(password.Trim());

    internal static bool IsSingleRepeatedCharacter(string password) =>
        password.Length > 0 && password.All(ch => ch == password[0]);
}

internal enum PasswordAuditSeverity
{
    Info = 0,
    Warning = 1,
    High = 2,
    Critical = 3
}

internal sealed record PasswordAuditFinding(
    Guid EntryId,
    string EntryTitle,
    PasswordAuditSeverity Severity,
    string Category,
    string Message);

internal sealed class PasswordAuditReport
{
    internal List<PasswordAuditFinding> Findings { get; } = [];

    internal int CriticalCount => Findings.Count(item => item.Severity == PasswordAuditSeverity.Critical);
    internal int HighCount => Findings.Count(item => item.Severity == PasswordAuditSeverity.High);
    internal int WarningCount => Findings.Count(item => item.Severity == PasswordAuditSeverity.Warning);
    internal int InfoCount => Findings.Count(item => item.Severity == PasswordAuditSeverity.Info);

    internal int AffectedEntries => Findings.Select(item => item.EntryId).Distinct().Count();
}

internal static class PasswordAuditor
{
    private static readonly TimeSpan OldPasswordAge = TimeSpan.FromDays(365);

    internal static PasswordAuditReport Analyze(IEnumerable<VaultEntry> sourceEntries)
    {
        ArgumentNullException.ThrowIfNull(sourceEntries);

        // Анализируем переданные снимки без дополнительного клонирования строк-секретов.
        List<VaultEntry> entries = sourceEntries.ToList();
        var report = new PasswordAuditReport();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (VaultEntry entry in entries)
        {
            AnalyzeSingleEntry(entry, now, report);
        }

        foreach (IGrouping<string, VaultEntry> group in entries
                     .Where(entry => !string.IsNullOrEmpty(entry.Password))
                     .GroupBy(entry => entry.Password, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            int reusedCount = group.Count();
            foreach (VaultEntry entry in group)
            {
                report.Findings.Add(new PasswordAuditFinding(
                    entry.Id,
                    entry.Title,
                    PasswordAuditSeverity.High,
                    "Повтор пароля",
                    $"Этот пароль используется в {reusedCount} записях."));
            }
        }

        report.Findings.Sort(static (left, right) =>
        {
            int severity = right.Severity.CompareTo(left.Severity);
            if (severity != 0) return severity;

            int title = StringComparer.CurrentCultureIgnoreCase.Compare(left.EntryTitle, right.EntryTitle);
            return title != 0 ? title : string.Compare(left.Category, right.Category, StringComparison.CurrentCultureIgnoreCase);
        });

        return report;
    }

    private static void AnalyzeSingleEntry(VaultEntry entry, DateTimeOffset now, PasswordAuditReport report)
    {
        if (string.IsNullOrEmpty(entry.Password))
        {
            report.Findings.Add(new PasswordAuditFinding(
                entry.Id,
                entry.Title,
                PasswordAuditSeverity.High,
                "Нет пароля",
                "В записи не указан пароль."));
        }
        else
        {
            if (PasswordSecurity.IsCommonPassword(entry.Password) || PasswordSecurity.IsSingleRepeatedCharacter(entry.Password))
            {
                report.Findings.Add(new PasswordAuditFinding(
                    entry.Id,
                    entry.Title,
                    PasswordAuditSeverity.Critical,
                    "Очень слабый пароль",
                    "Пароль относится к очевидным или легко угадываемым."));
            }
            else if (entry.Password.Length < 12)
            {
                report.Findings.Add(new PasswordAuditFinding(
                    entry.Id,
                    entry.Title,
                    PasswordAuditSeverity.High,
                    "Короткий пароль",
                    $"Длина пароля — {entry.Password.Length} символов; рекомендуется заменить его более длинным."));
            }
            else if (entry.Password.Length < 15)
            {
                report.Findings.Add(new PasswordAuditFinding(
                    entry.Id,
                    entry.Title,
                    PasswordAuditSeverity.Warning,
                    "Длина пароля",
                    $"Длина пароля — {entry.Password.Length} символов. Для новых паролей лучше использовать 15+ символов."));
            }

            DateTimeOffset passwordUpdated = entry.PasswordUpdatedUtc == default
                ? entry.UpdatedUtc
                : entry.PasswordUpdatedUtc;

            if (passwordUpdated != default && now - passwordUpdated >= OldPasswordAge)
            {
                int days = Math.Max(365, (int)(now - passwordUpdated).TotalDays);
                report.Findings.Add(new PasswordAuditFinding(
                    entry.Id,
                    entry.Title,
                    PasswordAuditSeverity.Info,
                    "Возраст пароля",
                    $"Пароль не менялся примерно {days} дней. Это информационный сигнал, а не требование периодической смены."));
            }
        }

        if (entry.Website.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            report.Findings.Add(new PasswordAuditFinding(
                entry.Id,
                entry.Title,
                PasswordAuditSeverity.Warning,
                "Небезопасный адрес",
                "Адрес сайта использует HTTP вместо HTTPS."));
        }
    }
}
