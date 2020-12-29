// CYPNode by Matthew Hellyer is licensed under CC BY-NC-ND 4.0.
// To view a copy of this license, visit https://creativecommons.org/licenses/by-nc-nd/4.0

using CYPNode.Services;

using Autofac;

namespace CYPNode.StartupExtensions
{
    public static class AppExtenstions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddTransactionService(this ContainerBuilder builder)
        {
            builder.RegisterType<TransactionService>().As<ITransactionService>();
            return builder;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static ContainerBuilder AddMempoolService(this ContainerBuilder builder)
        {
            builder.RegisterType<MempoolService>().As<IMempoolService>();
            return builder;
        }
    }
}
