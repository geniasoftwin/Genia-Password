namespace GeniaPassword.Models;

public sealed class VaultEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Website { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public List<CustomField> CustomFields { get; set; } = [];

    public List<PasswordHistoryItem> PasswordHistory { get; set; } = [];

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Дата последней фактической смены пароля. Нужна для локального аудита,
    /// не отправляется наружу и хранится только внутри зашифрованного vault.
    /// </summary>
    public DateTimeOffset PasswordUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString() => Title;

    /// <summary>
    /// Создаёт глубокую копию записи с сохранением оригинального Id.
    /// </summary>
    public VaultEntry Clone() => new()
    {
        Id = Id,
        Title = Title,
        Website = Website,
        UserName = UserName,
        Password = Password,
        Notes = Notes,
        UpdatedUtc = UpdatedUtc,
        PasswordUpdatedUtc = PasswordUpdatedUtc,
        PasswordHistory = [.. (PasswordHistory ?? []).Where(item => item is not null).Select(item => item.Clone())],
        CustomFields = [.. (CustomFields ?? []).Where(field => field is not null).Select(field => field.Clone())]
    };
}


public sealed class PasswordHistoryItem
{
    public string Password { get; set; } = string.Empty;

    public DateTimeOffset ActiveSinceUtc { get; set; }

    public DateTimeOffset ReplacedUtc { get; set; }

    public PasswordHistoryItem Clone() => new()
    {
        Password = Password,
        ActiveSinceUtc = ActiveSinceUtc,
        ReplacedUtc = ReplacedUtc
    };
}

public sealed class CustomField
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public bool IsSecret { get; set; }

    public CustomField Clone() => new()
    {
        Name = Name,
        Value = Value,
        IsSecret = IsSecret
    };
}

public sealed class VaultData
{
    public List<VaultEntry> Entries { get; set; } = [];
}

/// <summary>
/// JSON-конверт vault. Поля Nonce/Ciphertext/Tag используются и в v1, и в v2
/// для зашифрованных данных. Начиная с v2 мастер-пароль защищает только случайный
/// VaultKey через поля WrappedKey*, а сами записи шифруются VaultKey.
/// </summary>
public sealed class VaultEnvelope
{
    public int Version { get; set; }

    public string Kdf { get; set; } = string.Empty;

    public int Iterations { get; set; }

    public int MemoryKiB { get; set; }

    public int Parallelism { get; set; }

    public string Salt { get; set; } = string.Empty;

    public string WrappedKeyNonce { get; set; } = string.Empty;

    public string WrappedKey { get; set; } = string.Empty;

    public string WrappedKeyTag { get; set; } = string.Empty;

    public string Nonce { get; set; } = string.Empty;

    public string Ciphertext { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public long Revision { get; set; }

    public long SavedUtcUnixMilliseconds { get; set; }
}
