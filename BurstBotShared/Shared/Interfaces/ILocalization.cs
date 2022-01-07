namespace BurstBotShared.Shared.Interfaces;

public interface ILocalization<out TLocalization> where TLocalization : class
{
    TLocalization LoadCommandHelps()
    {
        foreach (var key in AvailableCommands.Keys)
        {
            AvailableCommands[key] = File.ReadAllText(AvailableCommands[key]);
        }

        return (this as TLocalization)!;
    }
    
    Dictionary<string, string> AvailableCommands { get; }
}