using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace rxcypnode.Monitor
{
    public class MonitorHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}