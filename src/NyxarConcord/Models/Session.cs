namespace NyxarConcord.Models;

/// <summary>
/// Estado global mínimo da sessão. Guarda o id do usuário atual para que os
/// modelos (Server/Room) consigam decidir permissões (ex.: quem é o admin)
/// sem depender da camada de ViewModel.
/// </summary>
public static class Session
{
    /// <summary>PeerId do usuário logado neste app.</summary>
    public static string SelfId { get; set; } = "";
}
