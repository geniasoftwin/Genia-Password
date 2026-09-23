using System.Security.Cryptography;

namespace GeniaPassword.Security;

internal static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}|;:,.<>?";
    private const string Ambiguous = "O0oIl1|`'\"";

    internal static string Generate(int length = 16) =>
        Generate(length, useUpper: true, useDigits: true, useSymbols: true, excludeAmbiguous: false);

    internal static string Generate(
        int length,
        bool useUpper,
        bool useDigits,
        bool useSymbols,
        bool excludeAmbiguous)
    {
        if (length is < 8 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Длина пароля должна быть от 8 до 128 символов.");
        }

        string lower = Filter(Lowercase, excludeAmbiguous);
        string upper = Filter(Uppercase, excludeAmbiguous);
        string digits = Filter(Digits, excludeAmbiguous);
        string symbols = Filter(Symbols, excludeAmbiguous);

        var pools = new List<string> { lower };
        if (useUpper)
        {
            pools.Add(upper);
        }
        if (useDigits)
        {
            pools.Add(digits);
        }
        if (useSymbols)
        {
            pools.Add(symbols);
        }

        pools.RemoveAll(static pool => pool.Length == 0);
        if (pools.Count == 0)
        {
            throw new InvalidOperationException("Выберите хотя бы одну группу символов.");
        }
        if (length < pools.Count)
        {
            throw new InvalidOperationException("Длина пароля слишком мала для выбранных групп символов.");
        }

        string allChars = string.Concat(pools);
        char[] password = new char[length];
        int index = 0;

        foreach (string pool in pools)
        {
            password[index++] = GetRandomChar(pool);
        }

        while (index < password.Length)
        {
            password[index++] = GetRandomChar(allChars);
        }

        Shuffle(password);
        string result = new(password);
        Array.Clear(password, 0, password.Length);
        return result;
    }

    private static string Filter(string source, bool excludeAmbiguous) =>
        excludeAmbiguous
            ? new string(source.Where(character => !Ambiguous.Contains(character)).ToArray())
            : source;

    private static char GetRandomChar(string characterSet)
    {
        int index = RandomNumberGenerator.GetInt32(characterSet.Length);
        return characterSet[index];
    }

    private static void Shuffle(char[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
