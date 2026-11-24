using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SemanticCache.Api.Features.Caching;

public static class VectorEncoding
{
    /// <summary>Redis expects a vector as raw little-endian FLOAT32 bytes.</summary>
    public static byte[] ToBytes(float[] vector)
    {
        return MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();
    }

    /// <summary>A summary for the request inspector, so 3 KB of binary never reaches the page.</summary>
    public static object Describe(float[]? vector)
    {
        if (vector is null)
        {
            return new { present = false };
        }

        var bytes = ToBytes(vector);

        return new
        {
            present = true,
            dimensions = vector.Length,
            type = "FLOAT32",
            bytes = bytes.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
        };
    }
}
