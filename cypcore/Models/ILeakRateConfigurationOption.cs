namespace CYPCore.Models
{
    public interface ILeakRateConfigurationOption
    {
        int LeakRate { get; set; }
        int LeakRateNumberOfSeconds { get; set; }
        int MaxFill { get; set; }
    }
}