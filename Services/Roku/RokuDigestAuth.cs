using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TinyCinema;

public static class RokuDigestAuth
{
    private static readonly Regex ChallengeParameterRegex = new(
        @"(\w+)=(?:""([^""]+)""|([^\s,]+))",
        RegexOptions.Compiled);

    public static string Md5Hex(string data)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static Dictionary<string, string> ParseDigestChallenge(string authHeader)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in ChallengeParameterRegex.Matches(authHeader))
        {
            parameters[match.Groups[1].Value] = match.Groups[2].Success
                ? match.Groups[2].Value
                : match.Groups[3].Value;
        }

        return parameters;
    }

    public static Dictionary<string, string> GenerateDigestResponse(
        string username,
        string password,
        string method,
        string path,
        IReadOnlyDictionary<string, string> challenge)
    {
        challenge.TryGetValue("realm", out var realm);
        challenge.TryGetValue("nonce", out var nonce);
        challenge.TryGetValue("qop", out var qop);
        challenge.TryGetValue("opaque", out var opaque);

        var ha1 = Md5Hex($"{username}:{realm}:{password}");
        var ha2 = Md5Hex($"{method}:{path}");

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = username,
            ["realm"] = realm ?? string.Empty,
            ["nonce"] = nonce ?? string.Empty,
            ["uri"] = path
        };

        if (qop is "auth" or "auth-int")
        {
            var nc = "00000001";
            var cnonce = RandomNumberGenerator.GetHexString(16, lowercase: true);
            var response = Md5Hex($"{ha1}:{nonce}:{nc}:{cnonce}:{qop}:{ha2}");

            result["qop"] = qop;
            result["nc"] = nc;
            result["cnonce"] = cnonce;
            result["response"] = response;
        }
        else
        {
            result["response"] = Md5Hex($"{ha1}:{nonce}:{ha2}");
        }

        if (!string.IsNullOrWhiteSpace(opaque))
            result["opaque"] = opaque;

        return result;
    }

    public static string FormatDigestHeader(IReadOnlyDictionary<string, string> parameters)
    {
        var parts = new List<string>();

        foreach (var (key, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            parts.Add(key is "nc" or "qop"
                ? $"{key}={value}"
                : $"{key}=\"{value}\"");
        }

        return $"Digest {string.Join(", ", parts)}";
    }
}
