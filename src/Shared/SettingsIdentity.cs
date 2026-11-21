#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VsCopilotChatVoice.Settings;

internal static class SettingsIdentity
{
    internal static string CanonicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0 ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
            throw new ArgumentException("An absolute Windows path is required.", nameof(path));
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            path = @"\\" + path.Substring(8);
        else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path.Substring(4);
        bool driveRooted = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/');
        bool uncRooted = path.StartsWith(@"\\", StringComparison.Ordinal);
        if (!driveRooted && !uncRooted)
            throw new ArgumentException("An absolute Windows path is required.", nameof(path));
        return Path.GetFullPath(path).ToUpperInvariant();
    }

    internal static string SolutionKey(string solutionPath)
    {
        string canonical = CanonicalPath(solutionPath);
        string extension = Path.GetExtension(canonical);
        string? root = Path.GetPathRoot(canonical);
        if (root == null || string.Equals(canonical.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A solution file path is required.", nameof(solutionPath));
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(canonical));
        var result = new StringBuilder(hash.Length * 2);
        foreach (byte value in hash) result.Append(value.ToString("X2"));
        return result.ToString();
    }
}