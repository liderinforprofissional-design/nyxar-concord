using System.Text.Json.Serialization;

namespace NyxarConcord.Models;

/// <summary>
/// Um amigo/contato conhecido, guardado localmente para aparecer na lista de
/// amigos mesmo quando estiver offline.
/// </summary>
public sealed class FriendRecord
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("handle")] public string Handle { get; set; } = "";
    [JsonPropertyName("avatar")] public string AvatarPath { get; set; } = "";
}
