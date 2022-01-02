namespace BurstBotShared.Shared;

public static class Constants
{
    public const string BurstLogo =
        "https://cdn.discordapp.com/attachments/811517007446671391/920263974187044874/logo.png";

    public const int StartingBet = 1;
    public const int BufferSize = 8192;
    public const string OutputFileName = "output.jpg";
    public const string AttachmentUri = $"attachment://{OutputFileName}";

    public const string CheckMark = "✅";
    public const string CrossMark = "❌";
    public const string PlayMark = "▶";

    public const string GameStarted =
        "<:burst_spade:910826637657010226> <:burst_heart:910826529511051284> **GAME STARTED!** <:burst_diamond:910826609576140821> <:burst_club:910826578336948234>";
}