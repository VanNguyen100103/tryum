using BCrypt.Net;
using Tryum.Application.Common.Interfaces;

namespace Tryum.Infrastructure.Services;

/// <summary>
/// BCrypt password hasher (work factor 12).
/// Hash format starts with "$2b$" – dễ nhận ra trong DB.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (SaltParseException)
        {
            // Hash trong DB bị hỏng / không đúng định dạng BCrypt => coi như sai mật khẩu.
            return false;
        }
    }
}