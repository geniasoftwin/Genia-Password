using System.Collections.ObjectModel;
using System.Security.Cryptography;
using GeniaPassword.Models;
using GeniaPassword.Security;

namespace GeniaPassword.Services;

public sealed class VaultService(string vaultPath) : IDisposable
{
    private VaultData? _data;
    private byte[]? _vaultKey;
    private byte[]? _unlockKey;
    private byte[]? _salt;
    private VaultKdfParameters _kdf;
    private long _revision;
    private bool _needsSecurityUpgrade;
    private byte[]? _knownFileHash;
    private bool _disposed;

    public bool IsUnlocked =>
        _data is not null && _vaultKey is not null && _unlockKey is not null && _salt is not null;

    public int EntryCount => RequireData().Entries.Count;

    public long Revision => _revision;

    public bool NeedsSecurityUpgrade => _needsSecurityUpgrade;

    public string VaultPath => vaultPath;

    public string BackupDirectory => Path.Combine(
        Path.GetDirectoryName(vaultPath) ?? AppContext.BaseDirectory,
        "backups");

    public int BackupCount
    {
        get
        {
            try
            {
                return Directory.Exists(BackupDirectory)
                    ? Directory.EnumerateFiles(BackupDirectory, "vault_*.pnb").Count()
                    : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public string SecuritySummary => string.Equals(_kdf.Name, PasswordKdf.Argon2idName, StringComparison.Ordinal)
        ? $"Vault v2 · AES-256-GCM · Argon2id {_kdf.MemoryKiB / 1024} MiB / t={_kdf.Iterations} / p={_kdf.Parallelism}"
        : "Vault v2 · AES-256-GCM";

    // Возвращаем глубокие копии, чтобы UI не мог случайно изменить незашифрованное состояние без Save().
    public ReadOnlyCollection<VaultEntry> Entries =>
        RequireData().Entries.Select(entry => entry.Clone()).ToList().AsReadOnly();

    public void Create(string masterPassword)
    {
        ThrowIfDisposed();
        MasterPasswordPolicy.ValidateForCreation(masterPassword);

        if (File.Exists(vaultPath))
        {
            throw new IOException("Файл хранилища уже существует. Создание поверх существующего файла запрещено.");
        }

        Lock();

        byte[] salt = VaultCrypto.CreateSalt();
        byte[] unlockKey = VaultCrypto.DeriveCurrentUnlockKey(masterPassword, salt);
        byte[] vaultKey = VaultCrypto.CreateVaultKey();

        _data = new VaultData();
        _salt = salt;
        _unlockKey = unlockKey;
        _vaultKey = vaultKey;
        _kdf = PasswordKdf.CurrentParameters;
        _revision = 0;
        _needsSecurityUpgrade = false;

        try
        {
            Save();
        }
        catch
        {
            Lock();
            throw;
        }
    }

    public void Unlock(string masterPassword)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(masterPassword);
        Lock();

        var fileInfo = new FileInfo(vaultPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Файл хранилища не найден.", vaultPath);
        }

        if (fileInfo.Length <= 0)
        {
            throw new VaultUnlockException("Файл хранилища пуст.");
        }

        if (fileInfo.Length > VaultCrypto.MaximumEnvelopeBytes)
        {
            throw new VaultUnlockException("Файл хранилища превышает допустимый размер.");
        }

        byte[] envelopeBytes = File.ReadAllBytes(vaultPath);
        if (envelopeBytes.LongLength > VaultCrypto.MaximumEnvelopeBytes)
        {
            CryptographicOperations.ZeroMemory(envelopeBytes);
            throw new VaultUnlockException("Файл хранилища превышает допустимый размер.");
        }

        try
        {
            VaultDecryptResult result = VaultCrypto.Decrypt(envelopeBytes, masterPassword);

            _data = result.Data;
            _vaultKey = result.VaultKey;
            _unlockKey = result.UnlockKey;
            _salt = result.Salt;
            _kdf = result.Kdf;
            _revision = result.Revision;
            _needsSecurityUpgrade = result.NeedsMigration;
            _knownFileHash = SHA256.HashData(envelopeBytes);

            // Старые v2-файлы с более слабым профилем Argon2id открываем штатно,
            // но пока мастер-пароль ещё доступен в этом методе, готовим новый
            // UnlockKey. TryUpgradeSecurity() затем перепривяжет тот же VaultKey
            // к усиленному KDF без перешифрования логики данных.
            if (!result.NeedsMigration && PasswordKdf.NeedsUpgrade(result.Kdf))
            {
                byte[] strongerSalt = VaultCrypto.CreateSalt();
                byte[] strongerUnlockKey = VaultCrypto.DeriveCurrentUnlockKey(masterPassword, strongerSalt);

                ZeroAndRelease(ref _unlockKey);
                ZeroAndRelease(ref _salt);

                _unlockKey = strongerUnlockKey;
                _salt = strongerSalt;
                _kdf = PasswordKdf.CurrentParameters;
                _needsSecurityUpgrade = true;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(envelopeBytes);
        }
    }

    /// <summary>
    /// После открытия старого v1 или v2 с устаревшим KDF пробует сразу
    /// обновить защиту. Ошибка обновления не блокирует чтение уже успешно
    /// расшифрованного vault: пользователь сможет повторить сохранение позже.
    /// </summary>
    public bool TryUpgradeSecurity(out string? errorMessage)
    {
        ThrowIfDisposed();
        RequireData();

        if (!_needsSecurityUpgrade)
        {
            errorMessage = null;
            return true;
        }

        try
        {
            Save();

            // После перехода с v1 или слабого v2 не оставляем рядом backup,
            // который всё ещё защищён старым/более дешёвым KDF.
            File.Copy(vaultPath, $"{vaultPath}.bak", overwrite: true);
            ResetRotatingBackupsToCurrentVault();

            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Меняет мастер-пароль без замены случайного VaultKey. Текущий пароль обязательно
    /// проверяется, чтобы оставленное разблокированным приложение нельзя было незаметно
    /// перепривязать к чужому мастер-паролю.
    /// </summary>
    public bool ChangeMasterPassword(string currentPassword, string newPassword)
    {
        ThrowIfDisposed();
        RequireData();
        ArgumentNullException.ThrowIfNull(currentPassword);
        if (currentPassword.Length == 0)
        {
            throw new VaultUnlockException("Введите текущий мастер-пароль.");
        }

        MasterPasswordPolicy.ValidateForCreation(newPassword);

        byte[] salt = _salt ?? throw new InvalidOperationException("Хранилище заблокировано.");
        byte[] unlockKey = _unlockKey ?? throw new InvalidOperationException("Хранилище заблокировано.");

        byte[] currentCandidate = PasswordKdf.DeriveArgon2id(
            currentPassword,
            salt,
            _kdf.Iterations,
            _kdf.MemoryKiB,
            _kdf.Parallelism,
            VaultCrypto.KeySize);

        try
        {
            if (!CryptographicOperations.FixedTimeEquals(currentCandidate, unlockKey))
            {
                throw new VaultUnlockException("Текущий мастер-пароль введён неверно.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentCandidate);
        }

        byte[] newSalt = VaultCrypto.CreateSalt();
        byte[] newUnlockKey = VaultCrypto.DeriveCurrentUnlockKey(newPassword, newSalt);
        byte[] oldSalt = salt;
        byte[] oldUnlockKey = unlockKey;

        _salt = newSalt;
        _unlockKey = newUnlockKey;
        _kdf = PasswordKdf.CurrentParameters;

        try
        {
            Save();
        }
        catch
        {
            _salt = oldSalt;
            _unlockKey = oldUnlockKey;
            CryptographicOperations.ZeroMemory(newSalt);
            CryptographicOperations.ZeroMemory(newUnlockKey);
            throw;
        }

        CryptographicOperations.ZeroMemory(oldSalt);
        CryptographicOperations.ZeroMemory(oldUnlockKey);

        // После смены мастер-пароля предыдущий .bak всё ещё был бы открываем старым
        // паролем. Перезаписываем backup текущей новой версией, чтобы старый пароль
        // перестал давать доступ и к резервной копии.
        try
        {
            File.Copy(vaultPath, $"{vaultPath}.bak", overwrite: true);
            ResetRotatingBackupsToCurrentVault();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void AddEntry(VaultEntry entry)
    {
        VaultData data = RequireData();
        if (data.Entries.Count >= VaultEntryRules.MaxEntries)
        {
            throw new InvalidOperationException($"Достигнут лимит: {VaultEntryRules.MaxEntries:N0} записей.");
        }

        VaultEntry storedEntry = VaultEntryRules.NormalizeAndValidate(entry);

        if (data.Entries.Any(existing => existing.Id == storedEntry.Id))
        {
            throw new InvalidOperationException("Запись с таким идентификатором уже существует.");
        }

        EnsureNoDuplicate(data, storedEntry, excludedId: null);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        storedEntry.UpdatedUtc = now;
        storedEntry.PasswordUpdatedUtc = now;
        storedEntry.PasswordHistory = [];
        data.Entries.Add(storedEntry);

        try
        {
            Save();
        }
        catch
        {
            data.Entries.Remove(storedEntry);
            throw;
        }
    }

    public void UpdateEntry(VaultEntry entry)
    {
        VaultData data = RequireData();
        int index = data.Entries.FindIndex(item => item.Id == entry.Id);
        if (index < 0)
        {
            throw new InvalidOperationException("Запись не найдена.");
        }

        VaultEntry replacement = VaultEntryRules.NormalizeAndValidate(entry);
        VaultEntry previous = data.Entries[index];

        // Старые версии могли уже содержать совпадающие учётные данные.
        // Разрешаем редактировать такую запись, пока пользователь не создаёт НОВОЕ совпадение.
        if (!SameCredential(previous, replacement))
        {
            EnsureNoDuplicate(data, replacement, replacement.Id);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool passwordChanged = !string.Equals(previous.Password, replacement.Password, StringComparison.Ordinal);
        replacement.UpdatedUtc = now;
        replacement.PasswordUpdatedUtc = passwordChanged
            ? now
            : NormalizePasswordUpdatedUtc(previous);
        replacement.PasswordHistory = [.. previous.PasswordHistory.Select(item => item.Clone())];

        if (passwordChanged && !string.IsNullOrEmpty(previous.Password))
        {
            replacement.PasswordHistory.Insert(0, new PasswordHistoryItem
            {
                Password = previous.Password,
                ActiveSinceUtc = NormalizePasswordUpdatedUtc(previous),
                ReplacedUtc = now
            });

            if (replacement.PasswordHistory.Count > VaultEntryRules.MaxPasswordHistory)
            {
                replacement.PasswordHistory.RemoveRange(
                    VaultEntryRules.MaxPasswordHistory,
                    replacement.PasswordHistory.Count - VaultEntryRules.MaxPasswordHistory);
            }
        }

        data.Entries[index] = replacement;

        try
        {
            Save();
        }
        catch
        {
            data.Entries[index] = previous;
            throw;
        }
    }

    public void DeleteEntry(Guid id)
    {
        VaultData data = RequireData();
        int index = data.Entries.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return;
        }

        VaultEntry removed = data.Entries[index];
        data.Entries.RemoveAt(index);

        try
        {
            Save();
        }
        catch
        {
            data.Entries.Insert(index, removed);
            throw;
        }
    }

    /// <summary>
    /// Удаляет только полностью идентичные записи. Различия в заметках или дополнительных полях
    /// считаются значимыми и никогда не отбрасываются автоматически.
    /// </summary>
    public int Deduplicate()
    {
        ThrowIfDisposed();
        VaultData data = RequireData();
        int initialCount = data.Entries.Count;

        List<VaultEntry> uniqueEntries = [.. data.Entries
            .GroupBy(entry => entry, VaultEntryContentComparer.Instance)
            .Select(group => group.OrderByDescending(item => item.UpdatedUtc).First())];

        if (uniqueEntries.Count == initialCount)
        {
            return 0;
        }

        List<VaultEntry> previousEntries = data.Entries;
        data.Entries = uniqueEntries;

        try
        {
            Save();
            return initialCount - uniqueEntries.Count;
        }
        catch
        {
            data.Entries = previousEntries;
            throw;
        }
    }

    public void Save()
    {
        ThrowIfDisposed();
        VaultData data = RequireData();
        byte[] vaultKey = _vaultKey ?? throw new InvalidOperationException("Хранилище заблокировано.");
        byte[] unlockKey = _unlockKey ?? throw new InvalidOperationException("Хранилище заблокировано.");
        byte[] salt = _salt ?? throw new InvalidOperationException("Хранилище заблокировано.");

        if (!string.Equals(_kdf.Name, PasswordKdf.Argon2idName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("В памяти находятся неподдерживаемые параметры защиты vault.");
        }

        EnsureVaultFileUnchanged();

        long nextRevision = checked(_revision + 1);
        long savedUtcUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] encrypted = VaultCrypto.Encrypt(
            data,
            vaultKey,
            unlockKey,
            salt,
            _kdf,
            nextRevision,
            savedUtcUnixMilliseconds);

        string? directory = Path.GetDirectoryName(vaultPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = $"{vaultPath}.tmp";
        string backupPath = $"{vaultPath}.bak";

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(encrypted);
                stream.Flush(true);
            }

            // Повторяем проверку непосредственно перед заменой: шифрование Argon2/AES
            // может занять время, и за этот интервал пользователь мог заменить vault
            // копией с другого устройства.
            EnsureVaultFileUnchanged();

            if (File.Exists(vaultPath))
            {
                CreateRotatingBackup(vaultPath, _revision);
                EnsureVaultFileUnchanged();
                ReplaceWithBackup(temporaryPath, vaultPath, backupPath);
            }
            else
            {
                // Первое сохранение: сразу создаём резервную копию рядом с основным vault.
                File.Move(temporaryPath, vaultPath);

                try
                {
                    File.Copy(vaultPath, backupPath, overwrite: false);
                }
                catch
                {
                    try
                    {
                        File.Delete(vaultPath);
                    }
                    catch
                    {
                        // Исходное исключение важнее ошибки очистки.
                    }

                    throw;
                }
            }

            byte[] expectedHash = SHA256.HashData(encrypted);
            byte[] actualHash = ComputeFileHash(vaultPath);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    throw new IOException("Проверка сохранённого vault.pnb по SHA-256 не прошла.");
                }

                ZeroAndRelease(ref _knownFileHash);
                _knownFileHash = expectedHash.ToArray();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(expectedHash);
                CryptographicOperations.ZeroMemory(actualHash);
            }

            // Метаданные меняем только после успешной замены и проверки файла.
            _revision = nextRevision;
            _needsSecurityUpgrade = false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            TryDeleteTemporaryFile(temporaryPath);
        }
    }

    public void Lock()
    {
        _data = null;

        ZeroAndRelease(ref _vaultKey);
        ZeroAndRelease(ref _unlockKey);
        ZeroAndRelease(ref _salt);
        ZeroAndRelease(ref _knownFileHash);

        _kdf = default;
        _revision = 0;
        _needsSecurityUpgrade = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Lock();
        _disposed = true;
    }

    private static DateTimeOffset NormalizePasswordUpdatedUtc(VaultEntry entry) =>
        entry.PasswordUpdatedUtc == default ? entry.UpdatedUtc : entry.PasswordUpdatedUtc;

    private static void EnsureNoDuplicate(VaultData data, VaultEntry candidate, Guid? excludedId)
    {
        bool isDuplicate = data.Entries.Any(existing =>
            (!excludedId.HasValue || existing.Id != excludedId.Value) &&
            SameCredential(existing, candidate));

        if (isDuplicate)
        {
            throw new InvalidOperationException("Такая запись уже существует в хранилище.");
        }
    }

    private static bool SameCredential(VaultEntry left, VaultEntry right) =>
        string.Equals(left.Title.Trim(), right.Title.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
        string.Equals(left.UserName.Trim(), right.UserName.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
        string.Equals(left.Website.Trim(), right.Website.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
        string.Equals(left.Password, right.Password, StringComparison.Ordinal);

    private void EnsureVaultFileUnchanged()
    {
        if (_knownFileHash is null)
        {
            if (File.Exists(vaultPath) && _revision == 0)
            {
                throw new VaultExternalChangeException(
                    "В папке появился другой vault.pnb. Сохранение остановлено, чтобы не перезаписать внешний файл.");
            }

            return;
        }

        if (!File.Exists(vaultPath))
        {
            throw new VaultExternalChangeException(
                "vault.pnb был удалён или перемещён после разблокировки. Сохранение остановлено.");
        }

        byte[] currentHash = ComputeFileHash(vaultPath);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(currentHash, _knownFileHash))
            {
                throw new VaultExternalChangeException(
                    "vault.pnb изменился вне GeniaPassword после разблокировки. " +
                    "Возможно, вы заменили файл версией с телефона. Закройте хранилище и откройте актуальный файл заново; " +
                    "автоматическая перезапись запрещена.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentHash);
        }
    }

    private void ResetRotatingBackupsToCurrentVault()
    {
        Directory.CreateDirectory(BackupDirectory);
        foreach (string existingBackup in Directory.EnumerateFiles(BackupDirectory, "vault_*.pnb"))
        {
            File.Delete(existingBackup);
        }

        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        string freshBackup = Path.Combine(BackupDirectory, $"vault_{timestamp}_rev{Math.Max(_revision, 0)}.pnb");
        File.Copy(vaultPath, freshBackup, overwrite: false);

        byte[] sourceHash = ComputeFileHash(vaultPath);
        byte[] backupHash = ComputeFileHash(freshBackup);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, backupHash))
            {
                TryDeleteTemporaryFile(freshBackup);
                throw new IOException("Проверка резервной копии после смены мастер-пароля не прошла.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sourceHash);
            CryptographicOperations.ZeroMemory(backupHash);
        }
    }

    private void CreateRotatingBackup(string sourcePath, long revision)
    {
        Directory.CreateDirectory(BackupDirectory);
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        string backupPath = Path.Combine(BackupDirectory, $"vault_{timestamp}_rev{Math.Max(revision, 0)}.pnb");

        File.Copy(sourcePath, backupPath, overwrite: false);

        byte[] sourceHash = ComputeFileHash(sourcePath);
        byte[] backupHash = ComputeFileHash(backupPath);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(sourceHash, backupHash))
            {
                TryDeleteTemporaryFile(backupPath);
                throw new IOException("Проверка резервной копии по SHA-256 не прошла.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sourceHash);
            CryptographicOperations.ZeroMemory(backupHash);
        }

        RotateBackups(BackupDirectory, keep: 10);
    }

    private static void RotateBackups(string directory, int keep)
    {
        try
        {
            FileInfo[] backups = new DirectoryInfo(directory)
                .EnumerateFiles("vault_*.pnb")
                .OrderByDescending(file => file.CreationTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToArray();

            foreach (FileInfo oldBackup in backups.Skip(keep))
            {
                try
                {
                    oldBackup.Delete();
                }
                catch
                {
                    // Невозможность удалить старую копию не должна портить новую.
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private static byte[] ComputeFileHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(stream);
    }

    private static void ReplaceWithBackup(string temporaryPath, string destinationPath, string backupPath)
    {
        try
        {
            File.Replace(temporaryPath, destinationPath, backupPath, true);
        }
        catch (PlatformNotSupportedException)
        {
            // Некоторые съёмные/сетевые файловые системы не поддерживают Replace.
            File.Copy(destinationPath, backupPath, overwrite: true);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    private VaultData RequireData()
    {
        ThrowIfDisposed();
        return _data ?? throw new InvalidOperationException("Хранилище заблокировано.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ZeroAndRelease(ref byte[]? buffer)
    {
        if (buffer is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(buffer);
        buffer = null;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Временный файл будет перезаписан при следующем сохранении.
        }
    }

    private sealed class VaultEntryContentComparer : IEqualityComparer<VaultEntry>
    {
        internal static readonly VaultEntryContentComparer Instance = new();

        public bool Equals(VaultEntry? x, VaultEntry? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;

            if (!string.Equals(x.Title.Trim(), y.Title.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                !string.Equals(x.Website.Trim(), y.Website.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                !string.Equals(x.UserName.Trim(), y.UserName.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                !string.Equals(x.Password, y.Password, StringComparison.Ordinal) ||
                !string.Equals(x.Notes, y.Notes, StringComparison.Ordinal))
            {
                return false;
            }

            if (x.CustomFields.Count != y.CustomFields.Count)
            {
                return false;
            }

            for (int i = 0; i < x.CustomFields.Count; i++)
            {
                CustomField left = x.CustomFields[i];
                CustomField right = y.CustomFields[i];
                if (!string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.CurrentCultureIgnoreCase) ||
                    !string.Equals(left.Value, right.Value, StringComparison.Ordinal) ||
                    left.IsSecret != right.IsSecret)
                {
                    return false;
                }
            }

            if (x.PasswordHistory.Count != y.PasswordHistory.Count)
            {
                return false;
            }

            for (int i = 0; i < x.PasswordHistory.Count; i++)
            {
                PasswordHistoryItem left = x.PasswordHistory[i];
                PasswordHistoryItem right = y.PasswordHistory[i];
                if (!string.Equals(left.Password, right.Password, StringComparison.Ordinal) ||
                    left.ActiveSinceUtc != right.ActiveSinceUtc ||
                    left.ReplacedUtc != right.ReplacedUtc)
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(VaultEntry obj)
        {
            var hash = new HashCode();
            hash.Add(obj.Title.Trim(), StringComparer.CurrentCultureIgnoreCase);
            hash.Add(obj.Website.Trim(), StringComparer.CurrentCultureIgnoreCase);
            hash.Add(obj.UserName.Trim(), StringComparer.CurrentCultureIgnoreCase);
            hash.Add(obj.Password, StringComparer.Ordinal);
            hash.Add(obj.Notes, StringComparer.Ordinal);

            foreach (CustomField field in obj.CustomFields)
            {
                hash.Add(field.Name.Trim(), StringComparer.CurrentCultureIgnoreCase);
                hash.Add(field.Value, StringComparer.Ordinal);
                hash.Add(field.IsSecret);
            }

            foreach (PasswordHistoryItem item in obj.PasswordHistory)
            {
                hash.Add(item.Password, StringComparer.Ordinal);
                hash.Add(item.ActiveSinceUtc);
                hash.Add(item.ReplacedUtc);
            }

            return hash.ToHashCode();
        }
    }
}


public sealed class VaultExternalChangeException(string message) : IOException(message);
