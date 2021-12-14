using DSharpPlus.Entities;

namespace BurstBotNET.Shared;

public static class Constants
{
    public const string BurstLogo =
        "https://cdn.discordapp.com/attachments/811517007446671391/920263974187044874/logo.png";
    
    public const int StartingBet = 1;
    
    public static readonly DiscordEmoji CheckMarkEmoji = DiscordEmoji.FromUnicode("✅");
    public static readonly DiscordEmoji CrossMarkEmoji = DiscordEmoji.FromUnicode("❌");
    public static readonly DiscordEmoji PlayMarkEmoji = DiscordEmoji.FromUnicode("▶");
}