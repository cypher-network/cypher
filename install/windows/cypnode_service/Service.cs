using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.IO;
using System.Threading;

namespace cypnode
{
    public partial class Service : ServiceBase
    {
        private Thread fThread;
        private bool fThreadActive;

        public Service()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            this.fThreadActive = true;
            ThreadStart job = new ThreadStart(this.WriteToLog);
            this.fThread = new Thread(job);
            this.fThread.Start();
        }

        protected override void OnStop()
        {
            this.fThreadActive = false;
            this.fThread.Join();
        }

        protected void WriteToLog()
        {
            string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string logDir = Path.Combine(appDataDir, "TestInstallerLogs");

            string logFile = Path.Combine(logDir, "serviceLog.txt");
            while(this.fThreadActive)
            {
                if(!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }
                using (var sw = new StreamWriter(logFile, true))
                {
                    sw.WriteLine("Log entry at {0}", DateTime.Now);
                }

                Thread.Sleep(5000);
            }
        }
    }
}
