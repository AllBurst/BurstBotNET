namespace BurstBotNET.Shared.Interfaces;

public interface IValueRealizable<out T>
{
    T GetValue();
}