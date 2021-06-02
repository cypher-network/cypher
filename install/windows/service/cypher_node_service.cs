using System;
using System.ServiceProcess;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;
using System.Management;

namespace service
{
    class CypNodeService : ServiceBase
    {
        private ManagementEventWatcher fSerfWatcher;
        private Process fProcess;
        private int fSerfPid;
        private Thread fThread;
        private bool fThreadActive;
        private const string fCommand = @"Cypher\Node\cypnode.exe";
        private string fCommandFullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), fCommand);

        protected override void OnStart(string[] args)
        {
            //while (!Debugger.IsAttached) Thread.Sleep(100); just for debugging so that process doesn't start before debugger is active
            fSerfWatcher = WatchForProcessStart("serf.exe");
            if (!this.fThreadActive)
            {
                this.fThreadActive = true;
                ThreadStart job = new ThreadStart(this.StartNode);
                this.fThread = new Thread(job);
                this.fThread.Start();
            }
            base.OnStart(args);
        }

        protected override void OnStop()
        {
            if (this.fThreadActive)
            {
                fProcess.Kill();
                this.fThreadActive = false;
                KillProcessAndChildren(fSerfPid);
            }
            base.OnStop();
        }

        protected void StartNode()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo() {
                FileName = fCommandFullPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fCommandFullPath)
            };
            Task<Result> task = Task.Run(() => RunAsync(startInfo));
            // Will block until the task is completed...
            Result result = task.Result;
        }

        public async Task<Result> RunAsync(ProcessStartInfo startInfo)
        { 
            Result result = new Result();
            using (fProcess = new Process() { StartInfo = startInfo, EnableRaisingEvents = true })
            {
                // List of tasks to wait for a whole fProcess exit
                List<Task> processTasks = new List<Task>();

                // === EXITED Event handling ===
                var processExitEvent = new TaskCompletionSource<object>();
                fProcess.Exited += (sender, args) =>
                {
                    processExitEvent.TrySetResult(true);
                };
                processTasks.Add(processExitEvent.Task);

                // === STDOUT handling ===
                var stdOutBuilder = new StringBuilder();
                if (fProcess.StartInfo.RedirectStandardOutput)
                {
                    var stdOutCloseEvent = new TaskCompletionSource<bool>();

                    fProcess.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                        {
                            stdOutCloseEvent.TrySetResult(true);
                        }
                        else
                        {
                            stdOutBuilder.AppendLine(e.Data);
                        }
                    };

                    processTasks.Add(stdOutCloseEvent.Task);
                }

                // === STDERR handling ===
                var stdErrBuilder = new StringBuilder();
                if (fProcess.StartInfo.RedirectStandardError)
                {
                    var stdErrCloseEvent = new TaskCompletionSource<bool>();

                    fProcess.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data == null)
                        {
                            stdErrCloseEvent.TrySetResult(true);
                        }
                        else
                        {
                            stdErrBuilder.AppendLine(e.Data);
                        }
                    };

                    processTasks.Add(stdErrCloseEvent.Task);
                }
                else
                {
                    // STDERR is not redirected, so we won't look for it
                }

                // === START OF PROCESS ===
                if (!fProcess.Start())
                {
                    result.ExitCode = fProcess.ExitCode;
                    return result;
                }

                // Reads the output stream first as needed and then waits because deadlocks are possible
                if (fProcess.StartInfo.RedirectStandardOutput)
                {
                    fProcess.BeginOutputReadLine();
                }
                else
                {
                    // No STDOUT
                }

                if (fProcess.StartInfo.RedirectStandardError)
                {
                    fProcess.BeginErrorReadLine();
                }
                else
                {
                    // No STDERR
                }

                // === ASYNC WAIT OF PROCESS ===

                // Process completion = exit AND stdout (if defined) AND stderr (if defined)
                Task processCompletionTask = Task.WhenAll(processTasks);

                // Task to wait for exit OR timeout (if defined)
                Task<Task> awaitingTask = Task.WhenAny(processCompletionTask);

                // Let's now wait for something to end...
                if ((await awaitingTask.ConfigureAwait(false)) == processCompletionTask)
                {
                    // -> Process exited cleanly
                    result.ExitCode = fProcess.ExitCode;
                }
                else
                {
                    // -> Timeout, let's kill the fProcess
                    try
                    {
                        fProcess.Kill();
                    }
                    catch
                    {
                        // ignored
                    }
                }

                // Read stdout/stderr
                result.StdOut = stdOutBuilder.ToString();
                result.StdErr = stdErrBuilder.ToString();
            }
            return result;
        }

        /// <summary>
        /// Run process result
        /// </summary>
        public class Result
        {
            /// <summary>
            /// Exit code
            /// <para>If NULL, process exited due to timeout</para>
            /// </summary>
            public int? ExitCode { get; set; } = null;

            /// <summary>
            /// Standard error stream
            /// </summary>
            public string StdErr { get; set; } = "";

            /// <summary>
            /// Standard output stream
            /// </summary>
            public string StdOut { get; set; } = "";
        }

        private ManagementEventWatcher WatchForProcessStart(string processName)
        {
            string queryString =
                "SELECT TargetInstance" +
                "  FROM __InstanceCreationEvent " +
                "WITHIN  10 " +
                " WHERE TargetInstance ISA 'Win32_Process' " +
                "   AND TargetInstance.Name = '" + processName + "'";

            // The dot in the scope means use the current machine
            string scope = @"\\.\root\CIMV2";

            // Create a watcher and listen for events
            ManagementEventWatcher watcher = new ManagementEventWatcher(scope, queryString);
            watcher.EventArrived += ProcessStarted;
            watcher.Start();
            return watcher;
        }

        private ManagementEventWatcher WatchForProcessEnd(string processName)
        {
            string queryString =
                "SELECT TargetInstance" +
                "  FROM __InstanceDeletionEvent " +
                "WITHIN  10 " +
                " WHERE TargetInstance ISA 'Win32_Process' " +
                "   AND TargetInstance.Name = '" + processName + "'";

            // The dot in the scope means use the current machine
            string scope = @"\\.\root\CIMV2";

            // Create a watcher and listen for events
            ManagementEventWatcher watcher = new ManagementEventWatcher(scope, queryString);
            watcher.EventArrived += ProcessEnded;
            watcher.Start();
            return watcher;
        }

        private void ProcessEnded(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
            string processName = targetInstance.Properties["Name"].Value.ToString();
            Console.WriteLine(String.Format("{0} process ended", processName));
        }

        private void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent.Properties["TargetInstance"].Value;
            string processName = targetInstance.Properties["Name"].Value.ToString();
            Process[] processlist = Process.GetProcesses();
            foreach (Process p in processlist)
            {
                if(p.ProcessName == "serf")
                {
                    fSerfPid = p.Id;
                    break;
                }
            }
        }

        /// <summary>
        /// Kill a process, and all of its children, grandchildren, etc.
        /// </summary>
        /// <param name="pid">Process ID.</param>
        private static void KillProcessAndChildren(int pid)
        {
            // Cannot close 'system idle process'.
            if (pid == 0)
            {
                return;
            }
            ManagementObjectSearcher searcher = new ManagementObjectSearcher
                    ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection moc = searcher.Get();
            foreach (ManagementObject mo in moc)
            {
                KillProcessAndChildren(Convert.ToInt32(mo["ProcessID"]));
            }
            try
            {
                Process proc = Process.GetProcessById(pid);
                proc.Kill();
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
        }
    }
}
