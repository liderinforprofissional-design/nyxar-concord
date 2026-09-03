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
    FileEnd,

    // --- Voz por WebRTC (mídia sai do relay e vai ponto-a-ponto) ---
    /// <summary>Oferta SDP de WebRTC (SDP no campo Text).</summary>
    RtcOffer,
    /// <summary>Resposta SDP de WebRTC (SDP no campo Text).</summary>
    RtcAnswer,
    /// <summary>Candidato ICE de WebRTC (JSON no campo Text).</summary>
    RtcIce,

    // --- Novos (adicionados ao FINAL para não renumerar os sinais frequentes) ---
    /// <summary>Atualização do servidor (nome/foto). Foto em PNG/base64 no campo Text.</summary>
    ServerUpdate,
    /// <summary>Estado do microfone (mutado/ativo). Text = "1" mutado, "0" ativo.</summary>
    MicState,
    /// <summary>Perfil do usuário (nome/foto). Foto em PNG/base64 no campo Text.</summary>
    UserUpdate,
    /// <summary>Áudio do computador na transmissão (PCM/base64) — tocado num buffer
    /// separado da voz, para não estourar o áudio de quem ouve.</summary>
    ScreenAudioFrame,
    /// <summary>Pedido para (re)enviar a foto de perfil e/ou a foto do servidor.
    /// Quem recebe responde direto para o solicitante — assim a foto se recupera
    /// mesmo que o anúncio inicial tenha se perdido.</summary>
    ProfileRequest,
    /// <summary>Lista de canais (salas) do servidor — o dono envia quando cria/exclui
    /// uma sala, para que ela apareça para todos. Canais em JSON no campo Payload.</summary>
    ServerChannels,
    /// <summary>Um espectador começou a assistir a MINHA transmissão (avisa o dono da tela).</summary>
    WatchStart,
    /// <summary>Um espectador parou de assistir a MINHA transmissão.</summary>
    WatchStop
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

    /// <summary>Início da call (Unix ms UTC), propagado nas mensagens de presença
    /// para todos mostrarem o mesmo cronômetro de duração da call.</summary>
    [JsonPropertyName("callStart")]
    public long? CallStart { get; set; }

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
    /// <summary>Arquivo salvo no histórico em disco (carregado sob demanda ao reabrir).</summary>
    [JsonIgnore] public string? FilePath { get; set; }

    /// <summary>Existe um arquivo salvo no histórico para este anexo?</summary>
    [JsonIgnore] public bool HasStoredFile => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);

    /// <summary>Bytes do anexo: os que já estão em memória ou lidos do histórico.</summary>
    public byte[]? LoadFileBytes()
    {
        if (FileData is not null) return FileData;
        try { return HasStoredFile ? System.IO.File.ReadAllBytes(FilePath!) : null; }
        catch { return null; }
    }

    [JsonIgnore]
    public string FileSizeLabel => FileSize >= 1024 * 1024 ? $"{FileSize / 1024.0 / 1024:0.#} MB"
        : FileSize >= 1024 ? $"{FileSize / 1024.0:0.#} KB" : $"{FileSize} B";

    /// <summary>Dá para salvar quando há bytes em memória OU um arquivo guardado no histórico.</summary>
    [JsonIgnore] public bool CanSaveFile => FileData is not null || HasStoredFile;

    /// <summary>Uma mensagem de texto normal (nem sistema, nem arquivo).</summary>
    [JsonIgnore] public bool IsText => !IsFile && !IsSystem;

    // --- Preview de link (card com título/descrição/imagem da página) ---
    /// <summary>Primeira URL http(s) encontrada no texto (ou null).</summary>
    [JsonIgnore]
    public string? FirstUrl
    {
        get
        {
            if (string.IsNullOrEmpty(Text)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(Text, @"https?://[^\s]+");
            return m.Success ? m.Value.TrimEnd('.', ',', ')', ']', '}') : null;
        }
    }

    /// <summary>Card de pré-visualização do link (preenchido de forma assíncrona).</summary>
    [JsonIgnore]
    public LinkPreview? Link { get; set; }

    // --- Tipo de anexo (imagem / vídeo mostram preview; o resto vira card comum) ---
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".mov", ".avi", ".mkv", ".m4v" };

    private bool HasExt(string[] exts)
    {
        var e = System.IO.Path.GetExtension(FileName)?.ToLowerInvariant() ?? "";
        return Array.IndexOf(exts, e) >= 0;
    }

    // Mostra preview de imagem/vídeo quando temos os bytes (ao vivo) OU o arquivo
    // guardado no histórico. Sem nenhum dos dois, cai no card comum.
    [JsonIgnore] public bool IsImageFile => IsFile && (FileData is not null || HasStoredFile) && HasExt(ImageExts);
    [JsonIgnore] public bool IsVideoFile => IsFile && (FileData is not null || HasStoredFile) && HasExt(VideoExts);
    /// <summary>Arquivo comum (nem imagem nem vídeo): mostra o card com botão "Salvar".</summary>
    [JsonIgnore] public bool IsOtherFile => IsFile && !IsImageFile && !IsVideoFile;

    /// <summary>Miniatura da imagem (construída dos bytes, em cache).</summary>
    private System.Windows.Media.ImageSource? _imagePreview;
    [JsonIgnore]
    public System.Windows.Media.ImageSource? ImagePreview
    {
        get
        {
            if (_imagePreview is not null || !IsImageFile) return _imagePreview;
            var bytes = LoadFileBytes();
            if (bytes is null) return null;
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = new System.IO.MemoryStream(bytes);
                bmp.DecodePixelWidth = 480; // miniatura leve
                bmp.EndInit();
                bmp.Freeze();
                _imagePreview = bmp;
            }
            catch { }
            return _imagePreview;
        }
    }

    /// <summary>Caminho temporário do vídeo (o MediaElement precisa de um arquivo), em cache.</summary>
    private string? _mediaPath;
    [JsonIgnore]
    public string? MediaPath
    {
        get
        {
            if (_mediaPath is not null || !IsVideoFile) return _mediaPath;
            try
            {
                // Se o arquivo já está salvo no histórico, aponta direto para ele.
                if (FileData is null && HasStoredFile)
                {
                    _mediaPath = new Uri(FilePath!).AbsoluteUri;
                    return _mediaPath;
                }
                if (FileData is null) return null;
                string ext = System.IO.Path.GetExtension(FileName);
                string p = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"nyxar-{Guid.NewGuid():N}{ext}");
                System.IO.File.WriteAllBytes(p, FileData);
                _mediaPath = new Uri(p).AbsoluteUri; // file:///... (o MediaElement resolve melhor)
            }
            catch { }
            return _mediaPath;
        }
    }
}
