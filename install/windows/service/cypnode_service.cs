using System;
using System.ServiceProcess;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text;

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
            if (!this.fThreadActive)
            {
                this.fThreadActive = true;
                ThreadStart job = new ThreadStart(this.StartNode);
                this.fThread = new Thread(job);
                this.fThread.Start();
            }
            Log("Starting");
            base.OnStart(args);
        }

        protected override void OnStop()
        {
            Log("Stopping");
            if (this.fThreadActive)
            {
                fProcess.Kill();
                this.fThreadActive = false;
            }
            base.OnStop();
        }

        protected void StartNode()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo() {
                FileName = fCommand,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
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
                else
                {
                    // STDOUT is not redirected, so we won't look for it
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
    }
}
