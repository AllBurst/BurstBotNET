using DSharpPlus.Entities;

namespace BurstBotNET.Shared;

public static class Constants
{
    public const string KotlinLogo =
        "https://cdn.discordapp.com/attachments/811517007446671391/901042591380938762/Kotlin_Full_Color_Logo_Mark_RGB.png";

    public const string BurstLogo =
        "https://cdn.discordapp.com/attachments/811517007446671391/920263974187044874/logo.png";
    
    public const int StartingBet = 1;
    
    public static readonly DiscordEmoji CheckMarkEmoji = DiscordEmoji.FromUnicode("✅");
    public static readonly DiscordEmoji CrossMarkEmoji = DiscordEmoji.FromUnicode("❌");
    public static readonly DiscordEmoji PlayMarkEmoji = DiscordEmoji.FromUnicode("▶");
}