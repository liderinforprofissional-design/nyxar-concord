using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Transferência de arquivos direta entre pares (sem servidor central).
///
/// PLANO DE IMPLEMENTAÇÃO:
///  1. Remetente envia um Signal (MessageKind.Signal) com metadados:
///     nome, tamanho, hash SHA-256 e um transferId.
///  2. Receptor aceita/recusa (Signal de resposta).
///  3. Abrir um socket TCP dedicado para o transferId e enviar os bytes em blocos
///     (ex.: 64 KB), mostrando progresso. TCP dedicado evita bloquear o chat.
///  4. Validar o hash ao final e salvar em disco.
///
/// Para grandes arquivos, prefira um stream separado ao invés do canal de chat.
/// </summary>
public interface IFileTransferService
{
    Task SendFileAsync(Peer peer, string filePath, IProgress<double>? progress = null, CancellationToken ct = default);

    event Action<Peer, FileOffer>? FileOffered;
    event Action<FileOffer, double>? TransferProgress;
    event Action<FileOffer, string>? TransferCompleted; // string = caminho salvo
}

public sealed class FileOffer
{
    public string TransferId { get; init; } = Guid.NewGuid().ToString("N");
    public string FileName { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = "";
}
