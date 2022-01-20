namespace BurstBotShared.Shared;

public static class Constants
{
    public const string BurstLogo =
        "https://cdn.discordapp.com/attachments/811517007446671391/920263974187044874/logo.png";

    public const string BurstGold =
        "https://cdn.discordapp.com/attachments/811517007446671391/931060212524257280/gold02.png";

    public const int StartingBet = 1;
    public const int BufferSize = 8192;
    public const string OutputFileName = "output.jpg";
    public const string AttachmentUri = $"attachment://{OutputFileName}";

    public const string CheckMark = "✅";
    public const string CrossMark = "❌";
    public const string PlayMark = "▶";
    public const string QuestionMark = "❓";

    public const string GameStarted =
        "<:burst_spade2:930749903158792192> <:burst_heart2:930749955914727474> **GAME STARTED!** <:burst_diamond2:930749987044851712> <:burst_club2:930750022167957504>";
}