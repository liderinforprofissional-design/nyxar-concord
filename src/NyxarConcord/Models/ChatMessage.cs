using System.Text.Json.Serialization;

namespace NyxarConcord.Models;

public enum MessageKind
{
    /// <summary>Anúncio de presença/handshake.</summary>
    Hello,
    /// <summary>Mensagem de texto normal.</summary>
    Text,
    /// <summary>Sinalização (convites de sala, voz, tela, etc.).</summary>
    Signal
}

public enum SignalType
{
    None,
    /// <summary>Convite para entrar num servidor (carrega os canais em Payload).</summary>
    ServerInvite,
    /// <summary>Aceite de convite / entrada no servidor.</summary>
    ServerJoin,
    /// <summary>Entrou num canal de áudio.</summary>
    RoomJoin,
    /// <summary>Saiu de um canal de áudio / fim de call.</summary>
    RoomLeave,
    /// <summary>Atualização de moderação de um canal (trancado/banidos) em Payload.</summary>
    ChannelUpdate,
    /// <summary>Um membro foi banido do servidor/canal.</summary>
    MemberBanned,
    /// <summary>Início de compartilhamento de tela.</summary>
    ScreenShareStart,
    /// <summary>Fim de compartilhamento de tela.</summary>
    ScreenShareStop,
    /// <summary>Um quadro (frame) de tela em JPEG/base64 (no campo Text).</summary>
    ScreenFrame,
    /// <summary>Um quadro de áudio (voz) em PCM/base64 (no campo Text).</summary>
    VoiceFrame,
    /// <summary>Início de envio de arquivo (metadados em Payload: id|nome|tamanho).</summary>
    FileOffer,
    /// <summary>Um pedaço do arquivo em base64 (Payload = id, Text = base64).</summary>
    FileChunk,
    /// <summary>Fim do envio do arquivo (Payload = id).</summary>
    FileEnd
}

/// <summary>
/// Mensagem trocada entre pares. Serializada em JSON, uma por linha (NDJSON),
/// sobre o socket TCP.
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("kind")]
    public MessageKind Kind { get; set; } = MessageKind.Text;

    [JsonPropertyName("senderId")]
    public string SenderId { get; set; } = "";

    [JsonPropertyName("senderName")]
    public string SenderName { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // --- Campos de sinalização (usados quando Kind == Signal) ---

    [JsonPropertyName("signal")]
    public SignalType Signal { get; set; } = SignalType.None;

    [JsonPropertyName("roomId")]
    public string? RoomId { get; set; }

    [JsonPropertyName("roomName")]
    public string? RoomName { get; set; }

    [JsonPropertyName("roomKind")]
    public RoomKind? RoomKind { get; set; }

    [JsonPropertyName("serverId")]
    public string? ServerId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    /// <summary>Carga extra em JSON (ex.: canais de um servidor, estado de moderação).</summary>
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }

    /// <summary>PeerId alvo (ex.: usuário banido).</summary>
    [JsonPropertyName("targetId")]
    public string? TargetId { get; set; }

    // --- Roteamento pelo relay (Cloudflare Worker) ---
    /// <summary>Destino direto (DM). Vazio = broadcast na sala do relay.</summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>Remetente injetado pelo Worker ao retransmitir.</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>Handle do remetente (para apresentação no relay).</summary>
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    /// <summary>True se a mensagem foi enviada por este usuário (uso na UI).</summary>
    [JsonIgnore]
    public bool IsMine { get; set; }

    /// <summary>Horário local formatado (HH:mm) para exibição.</summary>
    [JsonIgnore]
    public string TimeLabel => Timestamp.ToLocalTime().ToString("HH:mm");

    /// <summary>Iniciais do remetente, para o avatar.</summary>
    [JsonIgnore]
    public string SenderInitials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SenderName)) return "?";
            var parts = SenderName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    /// <summary>True para mensagens do "sistema" (avisos), estilizadas diferente.</summary>
    [JsonIgnore]
    public bool IsSystem => SenderName == "sistema";

    // --- Mensagem de arquivo ---
    [JsonIgnore] public bool IsFile { get; set; }
    [JsonIgnore] public string FileName { get; set; } = "";
    [JsonIgnore] public long FileSize { get; set; }
    [JsonIgnore] public byte[]? FileData { get; set; }

    [JsonIgnore]
    public string FileSizeLabel => FileSize >= 1024 * 1024 ? $"{FileSize / 1024.0 / 1024:0.#} MB"
        : FileSize >= 1024 ? $"{FileSize / 1024.0:0.#} KB" : $"{FileSize} B";

    /// <summary>Só há o que salvar quando o arquivo foi recebido (tem os bytes).</summary>
    [JsonIgnore] public bool CanSaveFile => FileData is not null;

    /// <summary>Uma mensagem de texto normal (nem sistema, nem arquivo).</summary>
    [JsonIgnore] public bool IsText => !IsFile && !IsSystem;
}
