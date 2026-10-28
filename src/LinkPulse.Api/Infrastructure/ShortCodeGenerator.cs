using System.Security.Cryptography;

namespace LinkPulse.Api.Infrastructure;

public sealed class ShortCodeGenerator
{
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    public const int DefaultLength = 7;

    // 62^7 is about 3.5 trillion combinations; collisions are still checked by the caller.
    public string Next(int length = DefaultLength) => RandomNumberGenerator.GetString(Alphabet, length);
}
