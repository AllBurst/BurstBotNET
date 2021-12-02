namespace BurstBotNET.Shared.Models.Data;

public class Player
{
    public string PlayerId { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Name { get; set; } = "";
    public Tip Tips { get; set; } = new();
}