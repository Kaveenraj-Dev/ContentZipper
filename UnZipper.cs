using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace ContentZipper
{
    /// <summary>
    /// This class provides the necessary functionality to UnZip the contents into a folder.
    /// </summary>
    public class UnZipper
    {
        // constructors


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sourcePath">The source path of the Zip file. </param>
        /// <param name="targetPath">The target path where the Zip file has to be unzipped. </param>
        /// <param name="progressUpdater">A callback delegate to know about the progress. </param>
        public UnZipper(string sourcePath,
                        string targetPath,
                        Action<object> progressUpdater = null)
        {
            this.sourcePath = sourcePath;
            this.targetPath = targetPath;
            this.progressUpdater = progressUpdater;
            initialize();
        }


        // public methods


        /// <summary>
        /// UnZip the contents.
        /// </summary>
        /// <returns></returns>
        public async Task UnZip()
        {
            // Check if the target folder is empty.
            DirectoryInfo directoryInfo = new DirectoryInfo(targetPath);
            if (directoryInfo.Exists && 
                (directoryInfo.GetDirectories().Length > 0 || directoryInfo.GetFiles().Length > 0))
            {
                exception = new IOException("Target must be an empty folder. ");
                zipStatus = ZipStatus.Error;
            }

            // UnZip the contents in a different thread.
            await Task.Run(() => unZipItems());
        }


        /// <summary>
        /// Cancels the UnZip Task.
        /// </summary>
        public void Cancel()
        {
            if (cancellationTokenSource != null)
            {
                cancellationTokenSource.Cancel();
            }
        }


        /// <summary>
        /// Returns the status of the UnZip operation.
        /// </summary>
        public ZipStatus ZipStatus
        {
            get
            {
                return zipStatus;
            }
        }


        /// <summary>
        /// Returns any exception occurred during the UnZip operation.
        /// </summary>
        public Exception Exception
        {
            get
            {
                return exception;
            }
        }


        /// <summary>
        /// Returns the total number of items to process.
        /// </summary>
        public int getTotalItemsToProcess()
        {
            return totalItems;
        }


        /// <summary>
        /// Returns the number of items processed.
        /// </summary>
        public int getProgress()
        {
            return progress;
        }


        /// <summary>
        /// Returns the current processing item's path.
        /// </summary>
        public string getProcessingItem()
        {
            return itemPath;
        }


        // private methods


        /// <summary>
        /// Initialize the fields.
        /// </summary>
        private void initialize()
        {
            this.zipStatus = ZipStatus.None;
            this.exception = null;
            this.totalItems = 0;
            this.progress = 0;
            this.itemPath = null;
        }


        /// <summary>
        /// UnZip the contents.
        /// </summary>
        private void unZipItems()
        {
            try
            {
                cancellationTokenSource = new CancellationTokenSource();

                CancellationToken cancellationToken = cancellationTokenSource.Token;

                using (var zipArchive = ZipFile.OpenRead(sourcePath))
                {
                    zipStatus = ZipStatus.InProcess;

                    ReadOnlyCollection<ZipArchiveEntry> entries = zipArchive.Entries;

                    totalItems = entries.Count;
                    int percentage = 0;
                    updateProgress(percentage);

                    foreach (ZipArchiveEntry entry in entries)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        string itemName = entry.FullName;
                        bool isFolder = itemName.EndsWith("/");

                        setProcessingItem(itemName);

                        if (isFolder)
                        {
                            string directoryFullName = Path.Combine(targetPath, itemName.Replace("/", "\\"));
                            if (!Directory.Exists(directoryFullName))
                                Directory.CreateDirectory(directoryFullName);
                        }
                        else
                        {
                            string fileFullName = Path.Combine(targetPath, itemName.Replace("/", "\\"));
                            if (!File.Exists(fileFullName))
                                entry.ExtractToFile(fileFullName, true);
                        }

                        incrementProgress();
                    }
                }

                zipStatus = ZipStatus.Finished;
            }
            catch (Exception exception)
            {
                doCleanup();

                if (exception is OperationCanceledException)
                {
                    zipStatus = ZipStatus.Cancelled;
                }
                else
                {
                    zipStatus = ZipStatus.Error;
                }
            }
            finally
            {
                // Final update.
                updateProgress(100);
            }
        }


        /// <summary>
        /// Sets the current item being processed.
        /// </summary>
        private void setProcessingItem(string itemPath)
        {
            this.itemPath = itemPath;
        }


        /// <summary>
        /// Increments the progress count and issues an notification to the caller.
        /// </summary>
        private void incrementProgress()
        {
            progress++;
            int percentage = (int)((double)progress / totalItems * 100);
            if (percentage > 100)
                percentage = 100;
            updateProgress(percentage);
        }


        /// <summary>
        /// Issues notification about the operation to the caller by invoking the callback method.
        /// </summary>
        private void updateProgress(int percentage)
        {
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested)
                return;

            if (progressUpdater != null)
            {
                progressUpdater.Invoke(percentage);
            }
        }


        /// <summary>
        /// Does a clean up in the target path.
        /// </summary>
        private void doCleanup()
        {
            // Clean up the target location.
            DirectoryInfo directoryInfo = new DirectoryInfo(targetPath);
            if (directoryInfo.Exists)
            {
                DirectoryInfo[] subDirectories = directoryInfo.GetDirectories();
                foreach (DirectoryInfo subDir in subDirectories)
                {
                    subDir.Delete(true);
                }

                FileInfo[] files = directoryInfo.GetFiles();
                foreach (FileInfo file in files)
                {
                    file.Delete();
                }
            }
        }


        // private instance fields

        private string sourcePath;
        private string targetPath;
        private CancellationTokenSource cancellationTokenSource;
        private Action<object> progressUpdater;
        private ZipStatus zipStatus;
        private Exception exception;
        private int totalItems;
        private int progress;
        private string itemPath;
    }
}
