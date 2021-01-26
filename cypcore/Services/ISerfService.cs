// CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

using CYPCore.Models;

namespace CYPCore.Services
{
    public interface ISerfService
    {
        Task StartAsync(IHostApplicationLifetime applicationLifetime);
        Task JoinSeedNodes(SeedNode seedNode);
    }
}