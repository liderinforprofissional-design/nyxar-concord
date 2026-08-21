using System.Net;

namespace NyxarConcord.Models;

/// <summary>
/// Representa outro usuário descoberto na rede (um "servidor local" remoto).
/// </summary>
public class Peer
{
    /// <summary>Identificador único do par (GUID gerado por instância).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Nome de exibição escolhido pelo usuário.</summary>
    public string DisplayName { get; set; } = "Desconhecido";

    /// <summary>Handle curto e legível, ex.: @carlos-1234.</summary>
    public string Handle { get; set; } = "";

    /// <summary>True se o par é alcançado pelo relay (Cloudflare), não por TCP direto.</summary>
    public bool IsRelay { get; set; }

    /// <summary>Endereço IP do par na rede.</summary>
    public IPAddress Address { get; set; } = IPAddress.Loopback;

    /// <summary>Porta TCP em que o servidor local do par escuta.</summary>
    public int Port { get; set; }

    /// <summary>Última vez que recebemos um anúncio deste par (para expiração).</summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    public IPEndPoint EndPoint => new(Address, Port);

    public override string ToString() => $"{DisplayName} ({Address}:{Port})";
}
