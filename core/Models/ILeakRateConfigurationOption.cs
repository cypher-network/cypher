namespace CypherNetwork.Models;

public interface ILeakRateConfigurationOption
{
    int LeakRate { get; set; }
    int LeakRateNumberOfSeconds { get; set; }
    int MemoryPoolMaxTransactions { get; set; }
}