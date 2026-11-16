using System.Security.Cryptography;

namespace Windbound.OrchestraRelay;

internal static class RelayProtocol
{
    internal const string HostPath = "/v1/host";
    internal const string JoinPath = "/v1/join";
    internal const string AttachPath = "/v1/attach";
    internal const int TokenLength = 48;
    internal const int MaxFollowers = 15;

    internal static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenLength / 2));
    internal static bool IsToken(string? token) =>
        token is { Length: TokenLength } && token.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
