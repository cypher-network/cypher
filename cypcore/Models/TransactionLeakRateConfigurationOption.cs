namespace CYPCore.Models
{
    public class TransactionLeakRateConfigurationOption : ILeakRateConfigurationOption
    {
        public int LeakRate { get; set; }
        public int LeakRateNumberOfSeconds { get; set; }
        public int MaxFill { get; set; }
    }
}