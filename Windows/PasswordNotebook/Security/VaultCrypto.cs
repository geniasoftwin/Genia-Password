using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GeniaPassword.Models;

namespace GeniaPassword.Security;

internal static class VaultCrypto
{
    internal const int LegacyFormatVersion = 1;
    internal const int CurrentFormatVersion = 2;

    internal const int LegacyDefaultIterations = 600_000;
    internal const int SaltSize = 16;
    internal const int KeySize = 32;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;

    internal const int MaximumCiphertextBytes = 50 * 1024 * 1024; // 50 MB
    internal const long MaximumEnvelopeBytes = 70L * 1024 * 1024; // JSON + Base64 overhead

    private const int MaximumBase64CiphertextLength = (MaximumCiphertextBytes / 3) * 4 + 4;

    private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 64
    };

    private static readonly JsonSerializerOptions DataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 64
    };

    internal static byte[] CreateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    internal static byte[] CreateVaultKey() => RandomNumberGenerator.GetBytes(KeySize);

    internal static byte[] DeriveCurrentUnlockKey(string password, byte[] salt)
    {
        VaultKdfParameters parameters = PasswordKdf.CurrentParameters;
        return PasswordKdf.DeriveArgon2id(
            password,
            salt,
            parameters.Iterations,
            parameters.MemoryKiB,
            parameters.Parallelism,
            KeySize);
    }

    /// <summary>
    /// Шифрует vault в формате v2: записи шифруются случайным VaultKey, а мастер-пароль
    /// через Argon2id защищает только VaultKey. Это позволяет в будущем менять способ
    /// разблокировки без перешифрования всего набора записей.
    /// </summary>
    internal static byte[] Encrypt(
        VaultData data,
        byte[] vaultKey,
        byte[] unlockKey,
        byte[] salt,
        VaultKdfParameters kdf,
        long revision,
        long savedUtcUnixMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateKey(vaultKey, nameof(vaultKey));
        ValidateKey(unlockKey, nameof(unlockKey));
        ValidateSalt(salt);

        if (!string.Equals(kdf.Name, PasswordKdf.Argon2idName, StringComparison.Ordinal))
        {
            throw new CryptographicException("Неподдерживаемый KDF для записи vault v2.");
        }

        PasswordKdf.ValidateArgon2Parameters(kdf.Iterations, kdf.MemoryKiB, kdf.Parallelism);
        if (revision <= 0 || savedUtcUnixMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Некорректная ревизия или дата сохранения vault.");
        }

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(data, DataJsonOptions);
        if (plaintext.Length > MaximumCiphertextBytes)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new InvalidOperationException("Хранилище превышает допустимый размер 50 МБ.");
        }

        try
        {
            string saltBase64 = Convert.ToBase64String(salt);
            byte[] headerAad = BuildV2HeaderAssociatedData(
                kdf,
                saltBase64,
                revision,
                savedUtcUnixMilliseconds);
            byte[] keyAad = AddPurpose(headerAad, "key-wrap");
            byte[] dataAad = AddPurpose(headerAad, "data");

            byte[] wrappedKeyNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] wrappedKey = new byte[KeySize];
            byte[] wrappedKeyTag = new byte[TagSize];

            using (var keyCipher = new AesGcm(unlockKey, TagSize))
            {
                keyCipher.Encrypt(wrappedKeyNonce, vaultKey, wrappedKey, wrappedKeyTag, keyAad);
            }

            byte[] dataNonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] dataTag = new byte[TagSize];

            using (var dataCipher = new AesGcm(vaultKey, TagSize))
            {
                dataCipher.Encrypt(dataNonce, plaintext, ciphertext, dataTag, dataAad);
            }

            var envelope = new VaultEnvelope
            {
                Version = CurrentFormatVersion,
                Kdf = kdf.Name,
                Iterations = kdf.Iterations,
                MemoryKiB = kdf.MemoryKiB,
                Parallelism = kdf.Parallelism,
                Salt = saltBase64,
                WrappedKeyNonce = Convert.ToBase64String(wrappedKeyNonce),
                WrappedKey = Convert.ToBase64String(wrappedKey),
                WrappedKeyTag = Convert.ToBase64String(wrappedKeyTag),
                Nonce = Convert.ToBase64String(dataNonce),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Tag = Convert.ToBase64String(dataTag),
                Revision = revision,
                SavedUtcUnixMilliseconds = savedUtcUnixMilliseconds
            };

            return JsonSerializer.SerializeToUtf8Bytes(envelope, EnvelopeJsonOptions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static VaultDecryptResult Decrypt(byte[] envelopeBytes, string password)
    {
        ArgumentNullException.ThrowIfNull(envelopeBytes);
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length == 0)
        {
            throw new ArgumentException("Мастер-пароль не может быть пустым.");
        }

        VaultEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaultEnvelope>(envelopeBytes, EnvelopeJsonOptions)
                ?? throw new VaultUnlockException("Файл хранилища пуст или повреждён.");
        }
        catch (JsonException exception)
        {
            throw new VaultUnlockException("Файл хранилища имеет неверный формат.", exception);
        }

        return envelope.Version switch
        {
            LegacyFormatVersion => DecryptLegacyAndPrepareMigration(envelope, password),
            CurrentFormatVersion => DecryptV2(envelope, password),
            _ => throw new VaultUnlockException($"Версия хранилища {envelope.Version} не поддерживается.")
        };
    }

    private static VaultDecryptResult DecryptV2(VaultEnvelope envelope, string password)
    {
        ValidateV2Envelope(envelope);

        if (string.IsNullOrEmpty(envelope.Ciphertext) || envelope.Ciphertext.Length > MaximumBase64CiphertextLength)
        {
            throw new VaultUnlockException("Файл хранилища повреждён или превышает допустимый размер.");
        }

        byte[] salt = ParseBase64(envelope.Salt, SaltSize, "salt");
        byte[] wrappedKeyNonce = ParseBase64(envelope.WrappedKeyNonce, NonceSize, "wrappedKeyNonce");
        byte[] wrappedKey = ParseBase64(envelope.WrappedKey, KeySize, "wrappedKey");
        byte[] wrappedKeyTag = ParseBase64(envelope.WrappedKeyTag, TagSize, "wrappedKeyTag");
        byte[] dataNonce = ParseBase64(envelope.Nonce, NonceSize, "nonce");
        byte[] dataTag = ParseBase64(envelope.Tag, TagSize, "tag");
        byte[] ciphertext = ParseBase64(envelope.Ciphertext, null, "ciphertext");

        if (ciphertext.Length > MaximumCiphertextBytes)
        {
            throw new VaultUnlockException("Файл хранилища слишком большой.");
        }

        var kdf = new VaultKdfParameters(
            envelope.Kdf,
            envelope.Iterations,
            envelope.MemoryKiB,
            envelope.Parallelism);

        byte[] unlockKey = PasswordKdf.DeriveArgon2id(
            password,
            salt,
            kdf.Iterations,
            kdf.MemoryKiB,
            kdf.Parallelism,
            KeySize);
        byte[] vaultKey = new byte[KeySize];
        byte[]? plaintext = null;

        try
        {
            byte[] headerAad = BuildV2HeaderAssociatedData(
                kdf,
                envelope.Salt,
                envelope.Revision,
                envelope.SavedUtcUnixMilliseconds);

            using (var keyCipher = new AesGcm(unlockKey, TagSize))
            {
                keyCipher.Decrypt(
                    wrappedKeyNonce,
                    wrappedKey,
                    wrappedKeyTag,
                    vaultKey,
                    AddPurpose(headerAad, "key-wrap"));
            }

            plaintext = new byte[ciphertext.Length];
            using (var dataCipher = new AesGcm(vaultKey, TagSize))
            {
                dataCipher.Decrypt(
                    dataNonce,
                    ciphertext,
                    dataTag,
                    plaintext,
                    AddPurpose(headerAad, "data"));
            }

            VaultData data = DeserializeAndNormalizeData(plaintext);
            return new VaultDecryptResult(
                data,
                vaultKey,
                unlockKey,
                salt,
                kdf,
                envelope.Revision,
                false);
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(vaultKey);
            CryptographicOperations.ZeroMemory(unlockKey);
            throw new VaultUnlockException(
                "Не удалось открыть хранилище. Проверьте мастер-пароль и целостность файла.",
                exception);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(vaultKey);
            CryptographicOperations.ZeroMemory(unlockKey);
            throw;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    /// <summary>
    /// Читает старый v1 (PBKDF2 -> AES-GCM напрямую), после чего в памяти сразу
    /// подготавливает новые Argon2id credentials и случайный VaultKey. При первом
    /// успешном Save() данные будут записаны уже как v2.
    /// </summary>
    private static VaultDecryptResult DecryptLegacyAndPrepareMigration(VaultEnvelope envelope, string password)
    {
        ValidateLegacyEnvelope(envelope);

        if (string.IsNullOrEmpty(envelope.Ciphertext) || envelope.Ciphertext.Length > MaximumBase64CiphertextLength)
        {
            throw new VaultUnlockException("Файл хранилища повреждён или превышает допустимый размер.");
        }

        byte[] legacySalt = ParseBase64(envelope.Salt, SaltSize, "salt");
        byte[] nonce = ParseBase64(envelope.Nonce, NonceSize, "nonce");
        byte[] tag = ParseBase64(envelope.Tag, TagSize, "tag");
        byte[] ciphertext = ParseBase64(envelope.Ciphertext, null, "ciphertext");

        if (ciphertext.Length > MaximumCiphertextBytes)
        {
            throw new VaultUnlockException("Файл хранилища слишком большой.");
        }

        byte[] legacyKey = PasswordKdf.DeriveLegacyPbkdf2(
            password,
            legacySalt,
            envelope.Iterations,
            KeySize);
        byte[]? plaintext = null;

        try
        {
            plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(legacyKey, TagSize))
            {
                aes.Decrypt(
                    nonce,
                    ciphertext,
                    tag,
                    plaintext,
                    BuildLegacyAssociatedData(envelope.Iterations));
            }

            VaultData data = DeserializeAndNormalizeData(plaintext);

            byte[] newSalt = CreateSalt();
            byte[] newUnlockKey = DeriveCurrentUnlockKey(password, newSalt);
            byte[] newVaultKey = CreateVaultKey();

            return new VaultDecryptResult(
                data,
                newVaultKey,
                newUnlockKey,
                newSalt,
                PasswordKdf.CurrentParameters,
                0,
                true);
        }
        catch (CryptographicException exception)
        {
            throw new VaultUnlockException(
                "Не удалось открыть хранилище. Проверьте мастер-пароль и целостность файла.",
                exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(legacyKey);
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    private static VaultData DeserializeAndNormalizeData(byte[] plaintext)
    {
        VaultData data;
        try
        {
            data = JsonSerializer.Deserialize<VaultData>(plaintext, DataJsonOptions)
                ?? throw new VaultUnlockException("Расшифрованное хранилище пусто.");
        }
        catch (JsonException exception)
        {
            throw new VaultUnlockException("Расшифрованные данные хранилища повреждены.", exception);
        }

        data.Entries ??= [];
        if (data.Entries.Count > VaultEntryRules.MaxEntries)
        {
            throw new VaultUnlockException(
                $"Хранилище содержит слишком много записей (максимум {VaultEntryRules.MaxEntries:N0}).");
        }

        var normalizedEntries = new List<VaultEntry>(data.Entries.Count);
        var ids = new HashSet<Guid>();
        foreach (VaultEntry? entry in data.Entries)
        {
            if (entry is null)
            {
                continue;
            }

            // Старые vault-файлы могли быть созданы до строгой валидации.
            // При открытии не отбрасываем пользовательские данные и не запрещаем старые дубли полей.
            entry.Title ??= string.Empty;
            entry.Website ??= string.Empty;
            entry.UserName ??= string.Empty;
            entry.Password ??= string.Empty;
            entry.Notes = VaultEntryRules.NormalizeLineEndings(entry.Notes ?? string.Empty);
            entry.CustomFields ??= [];
            entry.CustomFields = [.. entry.CustomFields.Where(field => field is not null)];
            entry.PasswordHistory ??= [];
            entry.PasswordHistory = [.. entry.PasswordHistory
                .Where(item => item is not null && !string.IsNullOrEmpty(item.Password))
                .Take(VaultEntryRules.MaxPasswordHistory)];

            foreach (CustomField field in entry.CustomFields)
            {
                field.Name ??= string.Empty;
                field.Value ??= string.Empty;
            }

            if (entry.UpdatedUtc == default)
            {
                entry.UpdatedUtc = DateTimeOffset.UtcNow;
            }

            if (entry.PasswordUpdatedUtc == default)
            {
                entry.PasswordUpdatedUtc = entry.UpdatedUtc;
            }

            if (entry.Id == Guid.Empty || !ids.Add(entry.Id))
            {
                do
                {
                    entry.Id = Guid.NewGuid();
                }
                while (!ids.Add(entry.Id));
            }

            normalizedEntries.Add(entry);
        }

        data.Entries = normalizedEntries;
        return data;
    }

    private static byte[] BuildLegacyAssociatedData(int iterations) =>
        Encoding.UTF8.GetBytes(
            $"GeniaPassword|{LegacyFormatVersion}|{PasswordKdf.LegacyPbkdf2Name}|{iterations}");

    private static byte[] BuildV2HeaderAssociatedData(
        VaultKdfParameters kdf,
        string saltBase64,
        long revision,
        long savedUtcUnixMilliseconds) =>
        Encoding.UTF8.GetBytes(
            $"GeniaPassword|{CurrentFormatVersion}|{kdf.Name}|{kdf.Iterations}|{kdf.MemoryKiB}|{kdf.Parallelism}|{saltBase64}|{revision}|{savedUtcUnixMilliseconds}");

    private static byte[] AddPurpose(byte[] headerAssociatedData, string purpose)
    {
        byte[] purposeBytes = Encoding.UTF8.GetBytes($"|{purpose}");
        byte[] result = new byte[headerAssociatedData.Length + purposeBytes.Length];
        Buffer.BlockCopy(headerAssociatedData, 0, result, 0, headerAssociatedData.Length);
        Buffer.BlockCopy(purposeBytes, 0, result, headerAssociatedData.Length, purposeBytes.Length);
        return result;
    }

    private static void ValidateLegacyEnvelope(VaultEnvelope envelope)
    {
        if (!string.Equals(envelope.Kdf, PasswordKdf.LegacyPbkdf2Name, StringComparison.Ordinal))
        {
            throw new VaultUnlockException("Алгоритм старого хранилища не поддерживается.");
        }

        if (envelope.Iterations is < PasswordKdf.LegacyMinimumIterations or > PasswordKdf.LegacyMaximumIterations)
        {
            throw new VaultUnlockException("Некорректные параметры формирования ключа старого хранилища.");
        }
    }

    private static void ValidateV2Envelope(VaultEnvelope envelope)
    {
        if (!string.Equals(envelope.Kdf, PasswordKdf.Argon2idName, StringComparison.Ordinal))
        {
            throw new VaultUnlockException("Алгоритм формирования ключа vault v2 не поддерживается.");
        }

        PasswordKdf.ValidateArgon2Parameters(envelope.Iterations, envelope.MemoryKiB, envelope.Parallelism);
        if (envelope.Revision <= 0 || envelope.SavedUtcUnixMilliseconds <= 0)
        {
            throw new VaultUnlockException("Метаданные ревизии vault v2 повреждены.");
        }
    }

    private static byte[] ParseBase64(string? value, int? expectedLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new VaultUnlockException($"Поле {fieldName} в хранилище отсутствует или пусто.");
        }

        if (expectedLength.HasValue)
        {
            // Не позволяем атакующему заставить приложение сначала декодировать огромную
            // Base64-строку для поля, которое по формату имеет фиксированный размер.
            int maximumEncodedLength = ((expectedLength.Value + 2) / 3) * 4;
            if (value.Length > maximumEncodedLength)
            {
                throw new VaultUnlockException($"Поле {fieldName} в хранилище имеет некорректную длину.");
            }
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            if (expectedLength.HasValue && bytes.Length != expectedLength.Value)
            {
                throw new VaultUnlockException($"Поле {fieldName} в хранилище имеет некорректную длину.");
            }

            return bytes;
        }
        catch (FormatException exception)
        {
            throw new VaultUnlockException($"Поле {fieldName} в хранилище повреждено (ошибка Base64).", exception);
        }
    }

    private static void ValidateKey(byte[] key, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Ключ должен иметь длину {KeySize} байт.", parameterName);
        }
    }

    private static void ValidateSalt(byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length != SaltSize)
        {
            throw new ArgumentException($"Соль должна иметь длину {SaltSize} байт.", nameof(salt));
        }
    }
}

internal sealed record VaultDecryptResult(
    VaultData Data,
    byte[] VaultKey,
    byte[] UnlockKey,
    byte[] Salt,
    VaultKdfParameters Kdf,
    long Revision,
    bool NeedsMigration);

internal sealed class VaultUnlockException : Exception
{
    internal VaultUnlockException(string message) : base(message)
    {
    }

    internal VaultUnlockException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
