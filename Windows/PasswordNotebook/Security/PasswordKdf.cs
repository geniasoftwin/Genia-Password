using System.Security.Cryptography;
using System.Text;
using TopSecret.Cryptography;

namespace GeniaPassword.Security;

internal readonly record struct VaultKdfParameters(
    string Name,
    int Iterations,
    int MemoryKiB,
    int Parallelism);

internal static class PasswordKdf
{
    internal const string LegacyPbkdf2Name = "PBKDF2-SHA256";
    internal const string Argon2idName = "ARGON2ID";

    internal const int LegacyMinimumIterations = 100_000;
    internal const int LegacyMaximumIterations = 5_000_000;

    // Единый профиль Windows/Android для новых и обновляемых vault v2.
    // 64 MiB / t=3 / p=2 остаётся комфортным для современных ПК и заметно
    // увеличивает стоимость офлайн-подбора мастер-пароля.
    internal const int DefaultArgon2Iterations = 3;
    internal const int DefaultArgon2MemoryKiB = 64 * 1024;
    internal const int DefaultArgon2Parallelism = 2;

    private const int MinimumArgon2Iterations = 2;
    private const int MaximumArgon2Iterations = 6;
    private const int MinimumArgon2MemoryKiB = 19 * 1024;
    private const int MaximumArgon2MemoryKiB = 128 * 1024;
    private const int MinimumArgon2Parallelism = 1;
    private const int MaximumArgon2Parallelism = 4;

    internal static VaultKdfParameters CurrentParameters => new(
        Argon2idName,
        DefaultArgon2Iterations,
        DefaultArgon2MemoryKiB,
        DefaultArgon2Parallelism);

    internal static bool NeedsUpgrade(VaultKdfParameters parameters) =>
        !string.Equals(parameters.Name, Argon2idName, StringComparison.Ordinal) ||
        parameters.Iterations < DefaultArgon2Iterations ||
        parameters.MemoryKiB < DefaultArgon2MemoryKiB ||
        parameters.Parallelism < DefaultArgon2Parallelism;

    internal static byte[] DeriveLegacyPbkdf2(string password, byte[] salt, int iterations, int outputBytes)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        if (password.Length == 0)
        {
            throw new ArgumentException("Пароль не может быть пустым.");
        }

        if (iterations is < LegacyMinimumIterations or > LegacyMaximumIterations)
        {
            throw new VaultUnlockException("Некорректные параметры формирования ключа PBKDF2.");
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            outputBytes);
    }

    internal static byte[] DeriveArgon2id(
        string password,
        byte[] salt,
        int iterations,
        int memoryKiB,
        int parallelism,
        int outputBytes)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);
        if (password.Length == 0)
        {
            throw new ArgumentException("Пароль не может быть пустым.");
        }

        ValidateArgon2Parameters(iterations, memoryKiB, parallelism);

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt.ToArray(),
                Iterations = iterations,
                MemorySize = memoryKiB,
                DegreeOfParallelism = parallelism
            };

            return argon2.GetBytes(outputBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }

    internal static void ValidateArgon2Parameters(int iterations, int memoryKiB, int parallelism)
    {
        if (iterations is < MinimumArgon2Iterations or > MaximumArgon2Iterations ||
            memoryKiB is < MinimumArgon2MemoryKiB or > MaximumArgon2MemoryKiB ||
            parallelism is < MinimumArgon2Parallelism or > MaximumArgon2Parallelism)
        {
            throw new VaultUnlockException("Некорректные параметры Argon2id в хранилище.");
        }
    }
}
