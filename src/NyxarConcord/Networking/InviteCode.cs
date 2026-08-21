using System.Text;
using System.Text.Json;

namespace NyxarConcord.Networking;

/// <summary>
/// Dados embutidos num código de convite para conexão fora da LAN.
/// </summary>
public sealed class InvitePayload
{
    public string PeerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; }
}

/// <summary>
/// Codifica/decodifica um código de convite (Base64 de JSON). O usuário compartilha
/// esse código com quem quer conectar pela internet.
///
/// NOTA: para funcionar pela internet de fato, o <see cref="InvitePayload.Host"/>
/// precisa ser o IP público + porta acessível (via port forwarding ou, no futuro,
/// NAT traversal com STUN/TURN — ver INatTraversalService).
/// </summary>
public static class InviteCode
{
    private const string Prefix = "NYX1-";

    public static string Encode(InvitePayload payload)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Prefix + Convert.ToBase64String(json);
    }

    public static InvitePayload? Decode(string code)
    {
        try
        {
            code = code.Trim();
            if (code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                code = code[Prefix.Length..];
            byte[] json = Convert.FromBase64String(code);
            return JsonSerializer.Deserialize<InvitePayload>(json);
        }
        catch
        {
            return null;
        }
    }
}
