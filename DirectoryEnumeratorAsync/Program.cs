namespace DirectoryEnumeratorAsync {

    #region Usings
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Configuration;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml.Linq;
    #endregion

    /// <summary>
    /// Asynchronously enumerates a specified file system path, and creates an XML report.
    /// </summary>
    class Program {

        #region Members
        /// <summary>
        /// Directories to exclude from enumeration.
        /// </summary>
        private static IReadOnlyList<string> DirectoryExclusions { get; set; }

        private static Stopwatch ProgressTimer { get; set; }

        private static int TasksCompleted { get; set; }

        private static int TasksCreated { get; set; }

        private static object TasksCreatedCompletedLockObject = new object();
        #endregion

        #region Methods
        private static void CreateReport(ConcurrentDictionary<string, FileInfo> fileSystemEntries) {

            var rootElement = new XElement($"ArrayOfFileSystemEntries");

            foreach (var fileInfo in fileSystemEntries.Values) {

                var fileInfoChildElement = new XElement("FileInfo");
                fileInfoChildElement.Add(new XElement("FullName", fileInfo.FullName));
                fileInfoChildElement.Add(new XElement("DirectoryName", fileInfo.DirectoryName));
                fileInfoChildElement.Add(new XElement("CreationTimeUtc", fileInfo.CreationTimeUtc.YMDHMSFriendly()));
                fileInfoChildElement.Add(new XElement("LastWriteTimeUtc", fileInfo.LastWriteTimeUtc.YMDHMSFriendly()));
                fileInfoChildElement.Add(new XElement("Size", fileInfo.Exists ? fileInfo.Length : -1));
                fileInfoChildElement.Add(new XElement("Attributes", fileInfo.Attributes));

                rootElement.Add(fileInfoChildElement);
            }

            var xDocument = new XDocument(new XDeclaration("1.0", "UTF-8", string.Empty), rootElement);
            var reportFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"DirectoryEnumeratorAsync-{DateTime.Now.ToString("yyyy-MM-dd-HH-mm")}.xml");
            Console.WriteLine($"Saving: {fileSystemEntries.Count.ToString("N0")} file system entries to report file: {reportFilePath}");
            xDocument.Save(reportFilePath);
        }

        /// <summary>
        /// Unhandled Exception Logger
        /// </summary>
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e) {
            Exception exception = e.ExceptionObject as Exception;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Unhandled Exception: {exception.VerboseExceptionString()}");
            Console.ResetColor();
        }

        private static async Task<ConcurrentDictionary<string, FileInfo>> GetFileSystemEntriesAsync(
            FileInfo pathFileInfo,
            ConcurrentBag<Task> tasks,
            IProgress<FileSystemEnumerationProgress> progress,
            IReadOnlyList<string> directoryExclusions = null,
            ConcurrentDictionary<string, FileInfo> fileSystemEntries = null,
            bool continueOnUnauthorizedAccessExceptions = true,
            bool continueOnPathTooLongExceptions = true) {

            #region Validation
            if (directoryExclusions == null) {
                directoryExclusions = new List<string>();
            }
            if (fileSystemEntries == null) {
                fileSystemEntries = new ConcurrentDictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            }
            if (pathFileInfo == null) {
                throw new ArgumentNullException("pathFileInfo");
            }
            #endregion

            try {
                await Task.Run(() => {

                    try {
                        lock (TasksCreatedCompletedLockObject) {
                            TasksCreated++;
                        }

                        if ((progress != null) && (fileSystemEntries.Count > 0)) {
                            lock (ProgressTimer) {
                                int progressTimerTotalSeconds = (int)ProgressTimer.Elapsed.TotalSeconds;
                                if (progressTimerTotalSeconds == 30) {
                                    ProgressTimer.Restart();
                                    progress.Report(new FileSystemEnumerationProgress() { FileSystemEntries = fileSystemEntries.Count, CurrentFilePath = pathFileInfo.FullName });
                                }
                            }
                        }

                        if (!pathFileInfo.Attributes.HasFlag(FileAttributes.Directory)) {
                            throw new ArgumentException($"Path is not a directory: {pathFileInfo.FullName}");
                        }

                        foreach (var fileSystemEntryPath in Directory.EnumerateFileSystemEntries(
                            pathFileInfo.FullName, "*", SearchOption.TopDirectoryOnly)) {

                            FileInfo childFileInfo = null;

                            childFileInfo = new FileInfo(fileSystemEntryPath);
                            FileInfo placeHolder = null;
                            fileSystemEntries.AddOrUpdate(childFileInfo.FullName, childFileInfo, (TKey, TOldValue) => placeHolder);

                            if (childFileInfo.Attributes.HasFlag(FileAttributes.Directory)
                                && !childFileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                                if (directoryExclusions.Any(x => childFileInfo.FullName.IndexOf(x, StringComparison.OrdinalIgnoreCase) > -1)) continue;

                                tasks.Add(Task.Run(async () => {
                                    await GetFileSystemEntriesAsync(
                                        pathFileInfo: childFileInfo,
                                        tasks: tasks,
                                        progress: progress,
                                        directoryExclusions: directoryExclusions,
                                        fileSystemEntries: fileSystemEntries,
                                        continueOnUnauthorizedAccessExceptions: continueOnUnauthorizedAccessExceptions,
                                        continueOnPathTooLongExceptions: continueOnPathTooLongExceptions);

                                }));
                            } // if (childFileInfo.Attributes.HasFlag()
                        } // foreach (var fileSystemEntryPath in Directory.EnumerateFileSystemEntries(
                    }
                    finally {
                        lock (TasksCreatedCompletedLockObject) {
                            TasksCompleted++;
                        }
                    }
                }); // await Task.Run(() => {
            }
            catch (UnauthorizedAccessException e) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} pathFileInfo.FullName: {pathFileInfo.FullName} UnauthorizedAccessException: {e.Message ?? "NULL"}");
                Console.ResetColor();
                if (!continueOnUnauthorizedAccessExceptions) throw;
            }
            catch (PathTooLongException e) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} pathFileInfo.FullName: {pathFileInfo.FullName} PathTooLongException: {e.Message ?? "NULL"}");
                Console.ResetColor();
                if (!continueOnPathTooLongExceptions) throw;
            }
            catch (AggregateException ae) {
                Debug.WriteLine($"InnerException count: {ae.Flatten().InnerExceptions.Count}");
            }
            catch (Exception e) {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} path: {pathFileInfo.FullName} child path: {pathFileInfo.FullName} Exception: {e.VerboseExceptionString()}");
                Console.ResetColor();
                throw;
            }

            return fileSystemEntries;
        }

        private static void Initialize() {

            #region DirectoryExclusions
            if (ConfigurationManager.AppSettings["DirectoryExclusions"] != null) {
                DirectoryExclusions = ConfigurationManager.AppSettings["DirectoryExclusions"]
                    .Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            }
            if (DirectoryExclusions == null) {
                DirectoryExclusions = new List<string>();
            }
            if (DirectoryExclusions.Count > 0) {
                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} DirectoryExclusions:");
                foreach (var directoryExclusion in DirectoryExclusions) {
                    Console.WriteLine($" - {directoryExclusion}");
                }
            }
            #endregion
        }

        static void Main(string[] args) {

            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            if (args.Length == 0) {
                Console.WriteLine("Usage: DirectoryEnumeratorAsync <path>");
                return;
            }

            var pathToEnumerate = args[0].Trim();
            if (pathToEnumerate.Contains("\"")) {
                pathToEnumerate = pathToEnumerate.Replace("\"", string.Empty);
            }

            var stopwatch = Stopwatch.StartNew();
            ProgressTimer = Stopwatch.StartNew();

            Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} Getting directories and files for path: {pathToEnumerate}");

            Initialize();

            using (new ProcessPrivileges.PrivilegeEnabler(Process.GetCurrentProcess(),
                ProcessPrivileges.Privilege.Backup, ProcessPrivileges.Privilege.SystemEnvironment)) {
                if (!Directory.Exists(pathToEnumerate)) {
                    Console.WriteLine($"Directory does not exist: {pathToEnumerate}");
                    return;
                }

                var tasks = new ConcurrentBag<Task>();

                var progress = new Progress<FileSystemEnumerationProgress>((reportedProgress) => {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} File system entries found: {reportedProgress.FileSystemEntries.ToString("N0")}; TasksCreated: {TasksCreated}; TasksCompleted: {TasksCompleted}; Total tasks: {tasks.Count.ToString("N0")}");
                    Console.WriteLine($" Current Path: {reportedProgress.CurrentFilePath}");
                    Console.ResetColor();
                });

                var getFileSystemEntriesTask = Task.Run(() =>
                        GetFileSystemEntriesAsync(
                            pathFileInfo: new FileInfo(pathToEnumerate),
                            tasks: tasks,
                            progress: progress,
                            directoryExclusions: DirectoryExclusions));

                tasks.Add(getFileSystemEntriesTask);

                while (true) {
                    Task.WaitAll(tasks.ToArray());
                    // wait a few seconds and check if the task count increases. If it increases, continue waiting.
                    int taskCount = tasks.Count;
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(10));
                    delayTask.Wait();

                    if ((tasks.Count == 0) || ((tasks.Count == taskCount) && tasks.All(x => x.IsCompleted))) break;
                }

                ConcurrentDictionary<string, FileInfo> fileSystemEntries = getFileSystemEntriesTask.Result;

                int directoryCount = fileSystemEntries.Where(x => x.Value.Attributes.HasFlag(FileAttributes.Directory)).Count();
                int reparsePointCount = fileSystemEntries.Where(x => x.Value.Attributes.HasFlag(FileAttributes.ReparsePoint)).Count();
                int fileCount = fileSystemEntries.Count - (directoryCount + reparsePointCount);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} Finished. Time required: {stopwatch.Elapsed.DHMSFriendly()} Total processor time: {Process.GetCurrentProcess().TotalProcessorTime.DHMSFriendly()} PEAK memory used: {Process.GetCurrentProcess().PeakWorkingSet64.ToString("N0")}");
                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} Total file system entries: {fileSystemEntries.Count} Files: {fileCount.ToString("N0")} Directories: {directoryCount.ToString("N0")} Reparse Points: {reparsePointCount.ToString("N0")}");
                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} TasksCreated: {TasksCreated} TasksCompleted: {TasksCompleted}");

                CreateReport(fileSystemEntries);
                Console.ResetColor();

            }
        }
        #endregion
    }
}
