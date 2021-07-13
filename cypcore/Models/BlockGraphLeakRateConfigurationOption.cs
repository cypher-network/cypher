namespace CYPCore.Models
{
    public class BlockGraphLeakRateConfigurationOption : ILeakRateConfigurationOption
    {
        public int LeakRate { get; set; }
        public int LeakRateNumberOfSeconds { get; set; }
        public int MaxFill { get; set; }
    }
}