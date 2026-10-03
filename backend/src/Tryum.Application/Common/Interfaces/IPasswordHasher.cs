namespace Tryum.Application.Common.Interfaces;

/// <summary>
/// Service hash mật khẩu (BCrypt/PBKDF2). Abstract để dễ test và đổi algorithm.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hash mật khẩu plain-text → string lưu vào DB.
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Kiểm tra password plain-text có match với hash không.
    /// </summary>
    bool Verify(string password, string passwordHash);
}