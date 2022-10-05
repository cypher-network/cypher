namespace CypherNetwork.Models;

public record TransactionLeakRateConfigurationOption : ILeakRateConfigurationOption
{
    public int LeakRate { get; set; }
    public int LeakRateNumberOfSeconds { get; set; }
    public int MemoryPoolMaxTransactions { get; set; }
}