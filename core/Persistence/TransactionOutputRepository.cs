// CypherNetwork by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CypherNetwork.Models;
using Serilog;

namespace CypherNetwork.Persistence;

/// <summary>
/// 
/// </summary>
public interface ITransactionOutputRepository : IRepository<TransactionOutput>
{

}

/// <summary>
/// 
/// </summary>
public class TransactionOutputRepository : Repository<TransactionOutput>, ITransactionOutputRepository
{
    private readonly ILogger _logger;
    private readonly IStoreDb _storeDb;

    public TransactionOutputRepository(IStoreDb storeDb, ILogger logger) : base(storeDb, logger)
    {
        _storeDb = storeDb;
        _logger = logger.ForContext("SourceContext", nameof(TransactionOutputRepository));

        SetTableName(StoreDb.TransactionOutputTable.ToString());
    }
}