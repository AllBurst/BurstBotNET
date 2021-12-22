namespace BurstBotShared.Shared.Interfaces;

public interface IValueRealizable<out T>
{
    T GetValue();
}