using System.Security.Cryptography;
using Identity.Application.Abstractions.Security;
using Identity.Application.Common.Interfaces;

namespace Identity.Infrastructure.Security;

public class PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;      // 128 bit
    private const int KeySize = 32;       // 256 bit
    private const int Iterations = 10000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;
    private const char SegmentDelimiter = ':';

    public string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Iterations,
            Algorithm,
            KeySize
        );

        return string.Join(
            SegmentDelimiter,
            Convert.ToHexString(hash),
            Convert.ToHexString(salt),
            Iterations,
            Algorithm
        );
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
            return false;
                
        string[] segments = passwordHash.Trim().Split(SegmentDelimiter);

        if (segments.Length != 4)
            return false;

        string hashHex = segments[0].Trim();
        string saltHex = segments[1].Trim();
        string iterStr = segments[2].Trim();
        string algoStr = segments[3].Trim();

        byte[] hash = Convert.FromHexString(hashHex);
        byte[] salt = Convert.FromHexString(saltHex);

        if (!int.TryParse(iterStr, out int iterations))
            return false;

        var algorithm = new HashAlgorithmName(algoStr);

        byte[] inputHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            algorithm,
            hash.Length
        );

        return CryptographicOperations.FixedTimeEquals(inputHash, hash);
    }

}
