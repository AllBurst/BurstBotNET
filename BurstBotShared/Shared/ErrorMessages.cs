namespace BurstBotShared.Shared;

public static class ErrorMessages
{
    public const string InvalidPlayer =
        "Sorry, but either one of the players you invited hasn't joined the server yet, or he doesn't have enough tips to join a game!";

    public const string HandleReactionFailed =
        "Failed to handle reaction from invited players";

    public const string PlayerStateNoGameProgress = "Player state doesn't have game progress.";
}