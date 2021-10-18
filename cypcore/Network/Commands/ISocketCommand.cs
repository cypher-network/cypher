//CYPCore by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using System.Threading.Tasks;

namespace CYPCore.Network.Commands
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISocketCommand<in TRequest>
    {
        Task Execute(byte[] key, string backendAddress, TRequest request);
    }
}