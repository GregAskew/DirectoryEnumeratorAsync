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

        /// <summary>
        /// Directories to exclude from enumeration.
        /// </summary>
        private static IReadOnlyList<string> DirectoryExclusions { get; set; }

        private static Stopwatch ElapsedTimer { get; set; }

        private static Stopwatch ProgressTimer { get; set; }

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

            if ((progress != null) && (fileSystemEntries.Count > 0)) {
                int progressTimerTotalSeconds = (int)ProgressTimer.Elapsed.TotalSeconds;
                if (progressTimerTotalSeconds == 60) {
                    ProgressTimer.Restart();
                    progress.Report(new FileSystemEnumerationProgress() { FileSystemEntries = fileSystemEntries.Count, CurrentFilePath = pathFileInfo.FullName });
                }
            }

            await Task.Run(() => {
                if (!pathFileInfo.Attributes.HasFlag(FileAttributes.Directory)) {
                    throw new ArgumentException($"Path is not a directory: {pathFileInfo.FullName}");
                }

                foreach (var fileSystemEntryPath in Directory.EnumerateFileSystemEntries(
                    pathFileInfo.FullName, "*", SearchOption.TopDirectoryOnly)) {
                    FileInfo childFileInfo = null;

                    try {
                        childFileInfo = new FileInfo(fileSystemEntryPath);
                        FileInfo placeHolder = null;
                        fileSystemEntries.AddOrUpdate(childFileInfo.FullName, childFileInfo, (TKey, TOldValue) => placeHolder);

                        if (childFileInfo.Attributes.HasFlag(FileAttributes.Directory)
                        && !childFileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                            if (directoryExclusions.Any(x => childFileInfo.FullName.IndexOf(x, StringComparison.OrdinalIgnoreCase) > -1)) continue;

                            tasks.Add(
                                Task.Factory.StartNew(
                                    async (x) => {
                                        await GetFileSystemEntriesAsync(
                                            childFileInfo, tasks, progress, directoryExclusions, fileSystemEntries);
                                    },
                                    state: childFileInfo.FullName,
                                    scheduler: TaskScheduler.Default,
                                    cancellationToken: CancellationToken.None,
                                    creationOptions: TaskCreationOptions.None
                                )
                            );
                        }
                    }
                    catch (UnauthorizedAccessException) {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} Insufficient permissions to access path: {fileSystemEntryPath}");
                        Console.ResetColor();
                        if (!continueOnUnauthorizedAccessExceptions) throw;
                    }
                    catch (PathTooLongException) {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} path too long: {fileSystemEntryPath}");
                        Console.ResetColor();
                        if (!continueOnPathTooLongExceptions) throw;
                    }
                    catch (Exception e) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[ThreadId: {Thread.CurrentThread.ManagedThreadId}] {Extensions.CurrentMethodName()} path: {pathFileInfo.FullName} child path: {fileSystemEntryPath} Exception: {e.VerboseExceptionString()}");
                        Console.ResetColor();
                        throw;
                    }
                }
            });

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

            ElapsedTimer = Stopwatch.StartNew();
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
                    Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} File system entries found: {reportedProgress.FileSystemEntries.ToString("N0")}; Total tasks: {tasks.Count.ToString("N0")}; Remaining tasks: {tasks.Where(x => !x.IsCompleted).Count().ToString("N0")}");
                    Console.WriteLine($" Current Path: {reportedProgress.CurrentFilePath}");
                });

                var getFileSystemEntriesTask = GetFileSystemEntriesAsync(
                    pathFileInfo: new FileInfo(pathToEnumerate),
                    tasks: tasks,
                    progress: progress,
                    directoryExclusions: DirectoryExclusions);

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

                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} Finished. Time required: {ElapsedTimer.Elapsed.HMSFriendly()} Total processor time: {Process.GetCurrentProcess().TotalProcessorTime.HMSFriendly()} PEAK memory used: {Process.GetCurrentProcess().PeakWorkingSet64.ToString("N0")}");
                Console.WriteLine($"{DateTime.Now.TimeOfDay.HMSFriendly()} Directories: {directoryCount.ToString("N0")} Files: {fileCount.ToString("N0")} Reparse Points: {reparsePointCount.ToString("N0")}");

                CreateReport(fileSystemEntries);

            }
        }
    }
}
