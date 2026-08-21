using System.Net;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// NAT traversal — permite que dois pares atrás de roteadores diferentes se
/// conectem pela internet, sem servidor central de relay (na medida do possível).
///
/// PLANO DE IMPLEMENTAÇÃO:
///  1. STUN: descobrir o IP:porta público desta máquina consultando um servidor
///     STUN (ex.: stun.l.google.com:19302). Assim cada par sabe seu endereço externo.
///  2. Troca de candidatos: os pares trocam seus endereços públicos/privados por um
///     canal de sinalização (pode ser um servidor de sinalização leve, um link/código
///     colado manualmente, ou um DHT no futuro).
///  3. UDP hole punching: ambos enviam pacotes um ao outro simultaneamente para abrir
///     as tabelas de NAT e estabelecer o caminho direto.
///  4. TURN (fallback): quando o hole punching falha (NAT simétrico), retransmite por
///     um servidor TURN. Isso deixa de ser 100% P2P, mas garante a conexão.
///
/// RECOMENDAÇÃO: use a biblioteca SIPSorcery, que implementa ICE/STUN/TURN completo
/// e integra com o transporte de mídia de voz e tela.
/// </summary>
public interface INatTraversalService
{
    /// <summary>Descobre o endpoint público (STUN).</summary>
    Task<IPEndPoint?> DiscoverPublicEndpointAsync(CancellationToken ct = default);

    /// <summary>Tenta abrir um caminho direto até o par (hole punching).</summary>
    Task<bool> TryPunchAsync(Peer peer, IPEndPoint remotePublic, CancellationToken ct = default);
}
