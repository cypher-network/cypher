using System.ServiceProcess;

namespace service
{
    class Program
    {
        static void Main(string[] args)
        {
            ServiceBase.Run(new CypNodeService());
        }
    }
}
