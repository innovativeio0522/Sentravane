using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Sentravane.Server.Web.Data.Entities;

namespace Sentravane.Server.Web.Services;

public interface IPasswordHasherService
{
    string HashPassword(string password);
    bool VerifyPassword(string hashedPassword, string providedPassword);
}

public class PasswordHasherService : IPasswordHasherService
{
    private const int SaltByteSize = 16;
    private const int HashByteSize = 32;
    private const int Iterations = 100_000;

    public string HashPassword(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteSize);
        byte[] hash = KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA512,
            iterationCount: Iterations,
            numBytesRequested: HashByteSize);

        var combinedBytes = new byte[SaltByteSize + HashByteSize];
        Buffer.BlockCopy(salt, 0, combinedBytes, 0, SaltByteSize);
        Buffer.BlockCopy(hash, 0, combinedBytes, SaltByteSize, HashByteSize);

        return Convert.ToBase64String(combinedBytes);
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        try
        {
            byte[] combinedBytes = Convert.FromBase64String(hashedPassword);
            if (combinedBytes.Length != SaltByteSize + HashByteSize) return false;

            byte[] salt = new byte[SaltByteSize];
            byte[] storedHash = new byte[HashByteSize];
            Buffer.BlockCopy(combinedBytes, 0, salt, 0, SaltByteSize);
            Buffer.BlockCopy(combinedBytes, SaltByteSize, storedHash, 0, HashByteSize);

            byte[] computedHash = KeyDerivation.Pbkdf2(
                password: providedPassword,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA512,
                iterationCount: Iterations,
                numBytesRequested: HashByteSize);

            return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
        }
        catch
        {
            return false;
        }
    }
}
