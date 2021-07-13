using CYPCore.Models;
using Serilog;

namespace CYPCore.Persistence
{
    public interface IImageRepository : IRepository<Image>
    {
    }

    public class ImageRepository : Repository<Image>, IImageRepository
    {
        private readonly ILogger _logger;

        public ImageRepository(IStoreDb storeDb, ILogger logger) : base(storeDb, logger)
        {
            _logger = logger.ForContext("SourceContext", nameof(ImageRepository));
            SetTableName(StoreDb.KeyImageTable.ToString());
        }
    }
}