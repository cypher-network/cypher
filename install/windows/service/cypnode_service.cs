using System;
using System.ServiceProcess;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace service
{
    class CypNodeService : ServiceBase
    {
        private Process fProcess;
        private Thread fThread;
        private bool fThreadActive;
        private const string fLogfile = @"Tangram\Node\Service\servicelog.txt";
        private const string fCommand = @"Tangram\Node\cypnode.exe";
        private string fLogFileLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), fLogfile);

        private void Log(string logMessage)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fLogFileLocation));
            File.AppendAllText(fLogFileLocation, DateTime.UtcNow.ToString() + " : " + logMessage + Environment.NewLine);
        }

        protected override void OnStart(string[] args)
        {
            this.fThreadActive = true;
            ThreadStart job = new ThreadStart(this.StartNode);
            this.fThread = new Thread(job);
            this.fThread.Start();
            Log("Starting");
            base.OnStart(args);
        }

        protected override void OnStop()
        {
            Log("Stopping");
            base.OnStop();
        }

        protected override void OnPause()
        {
            Log("Pausing");
            base.OnPause();
        }

        protected void StartNode()
        {
            fProcess.StartInfo.FileName = fCommand;
            fProcess.StartInfo.UseShellExecute = false;
            fProcess.StartInfo.RedirectStandardInput = true;
            fProcess.StartInfo.RedirectStandardOutput = true;
            fProcess.StartInfo.RedirectStandardError = true;
            fProcess.StartInfo.CreateNoWindow = true;
        }
    }
}
