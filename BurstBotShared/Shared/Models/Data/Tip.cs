namespace BurstBotShared.Shared.Models.Data;

public class Tip
{
    public long Amount { get; set; }
    public DateTime NextDailyReward { get; set; }
    public DateTime NextWeeklyReward { get; set; }
}