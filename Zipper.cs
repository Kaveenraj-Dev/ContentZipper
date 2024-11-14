using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace ContentZipper
{
    /// <summary>
    /// Zip operation statuses.
    /// </summary>
    public enum ZipStatus
    {
        None,
        InProcess,
        Finished,
        Cancelled,
        Error
    }


    /// <summary>
    /// Various options on how the directory contents are to be processed.
    /// </summary>
    [Flags]
    public enum DirectoryProcessingOptions
    {
        Default = 1,
        IncludeBaseDirectory = 2,
        IncludeSubDirectoryItems = 4
    }


    /// <summary>
    /// This class provides the necessary functionality to Zip the contents into a folder.
    /// </summary>
    public class Zipper
    {
        // constructors


        /// <summary>
        /// 
        /// </summary>
        /// <param name="sourcePath">The source path of the file or a folder. </param>
        /// <param name="targetPath">The target path for the Zip file. </param>
        /// <param name="isFolderPath">Specify whether the source path is a folder path or a file path. </param>
        /// <param name="overwriteTargetIfExists">Specify whether to override the target file if it already exists or not. </param>
        /// <param name="directoryProcessingOptions">Specify how the directory (sourcePath is a folder path) must be processed for the Zip opertion. </param>
        /// <param name="progressUpdater">A callback delegate to know about the progress. </param>
        public Zipper(  string sourcePath,
                        string targetPath,
                        bool isFolderPath,
                        bool overwriteTargetIfExists,
                        DirectoryProcessingOptions directoryProcessingOptions = DirectoryProcessingOptions.Default,
                        Action<object> progressUpdater = null)
        {
            this.sourcePath = sourcePath;
            this.targetPath = targetPath;
            this.isFolderPath = isFolderPath;
            this.overwriteTargetIfExists = overwriteTargetIfExists;
            this.directoryProcessingOptions = directoryProcessingOptions;
            this.progressUpdater = progressUpdater;
            initialize();
        }


        // public methods

        /// <summary>
        /// Zip the contents.
        /// </summary>
        /// <param name="compressionLevel">Specify the compression level for the operation. </param>
        /// <returns></returns>
        public async Task Zip(CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            // If the target Zip file already exists, take necessary action.
            if (new FileInfo(targetPath).Exists)
            {
                if (overwriteTargetIfExists)
                {
                    File.Delete(targetPath);
                }
                else
                {
                    exception = new IOException("Target file already exists. Please pass 'overwriteTargetIfExists' parameter as TRUE in the constructor and try again. ");
                    zipStatus = ZipStatus.Error;
                }
            }

            // Zip the contents in a different thread.
            await Task.Run(() => zipItems(compressionLevel));
        }


        /// <summary>
        /// Cancels the Zip Task.
        /// </summary>
        public void Cancel()
        {
            // We cannot cancel the zip operation of a single file.
            if (isFolderPath)
            {
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }
            }
        }


        /// <summary>
        /// Returns the status of the Zip operation.
        /// </summary>
        public ZipStatus ZipStatus
        {
            get
            {
                return zipStatus;
            }
        }


        /// <summary>
        /// Returns any exception occurred during the Zip operation.
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
        /// Zip the contents.
        /// </summary>
        private void zipItems(CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            try
            {
                // Proceed with the Zip operation.
                if (!isFolderPath)
                {
                    // This is a single file path.
                    FileInfo fileInfo = new FileInfo(sourcePath);

                    totalItems = 1;

                    using (ZipArchive zipArchive = ZipFile.Open(targetPath, ZipArchiveMode.Create))
                    {
                        setProcessingItem(sourcePath);

                        zipStatus = ZipStatus.InProcess;

                        int percentage = 0;
                        updateProgress(percentage);

                        zipArchive.CreateEntryFromFile(fileInfo.FullName, fileInfo.Name, compressionLevel);

                        incrementProgress();
                    }
                }
                else
                {
                    // This is a folder path.
                    cancellationTokenSource = new CancellationTokenSource();

                    DirectoryInfo directoryInfo = new DirectoryInfo(sourcePath);

                    bool includeBaseDirectory = directoryProcessingOptions.HasFlag(DirectoryProcessingOptions.IncludeBaseDirectory);
                    bool includeSubDirectoryItems = directoryProcessingOptions.HasFlag(DirectoryProcessingOptions.IncludeSubDirectoryItems);

                    // Calculate the items count.
                    totalItems = includeBaseDirectory ? 1 : 0;
                    
                    if (includeSubDirectoryItems)
                    {
                        int itemsInDirectory = directoryInfo.GetDirectories("*", SearchOption.AllDirectories).Length + directoryInfo.GetFiles("*.*", SearchOption.AllDirectories).Length;
                        totalItems += itemsInDirectory;
                    }
                    else
                    {
                        int itemsInBaseDirectory = directoryInfo.GetFiles().Length;
                        totalItems += itemsInBaseDirectory;
                    }

                    using (var zipArchive = ZipFile.Open(targetPath, ZipArchiveMode.Create))
                    {
                        zipStatus = ZipStatus.InProcess;

                        int percentage = 0;
                        updateProgress(percentage);

                        zipDirectory(sourcePath, zipArchive, directoryInfo.Name, compressionLevel, directoryProcessingOptions, cancellationTokenSource.Token);
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
        /// Zip the given directory.
        /// </summary>
        private void zipDirectory(string sourceDirectory, 
                                    ZipArchive zipArchive, 
                                    string directoryKey, 
                                    CompressionLevel compressionLevel, 
                                    DirectoryProcessingOptions directoryProcessingOptions, 
                                    CancellationToken cancellationToken)
        {
            bool includeBaseDirectory = directoryProcessingOptions.HasFlag(DirectoryProcessingOptions.IncludeBaseDirectory);
            bool includeSubDirectoryItems = directoryProcessingOptions.HasFlag(DirectoryProcessingOptions.IncludeSubDirectoryItems);

            string entryPrefix = includeBaseDirectory ? directoryKey + "/" : "";

            if (includeBaseDirectory)
            {
                setProcessingItem(sourceDirectory);

                // Add the current directory.
                zipArchive.CreateEntry(entryPrefix, compressionLevel);

                incrementProgress();
            }

            // Process all the files under this folder.
            string[] files = Directory.GetFiles(sourceDirectory, "*.*");

            foreach (string file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                setProcessingItem(file);

                string entryName = entryPrefix + new FileInfo(file).Name;
                zipArchive.CreateEntryFromFile(file, entryName, compressionLevel);

                incrementProgress();
            }

            if (includeSubDirectoryItems)
            {
                // Process all the sub-directories under this folder.
                string[] subDirectories = Directory.GetDirectories(sourceDirectory);

                foreach (string subDir in subDirectories)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    string subDirectoryName = new DirectoryInfo(subDir).Name;

                    // For sub directories, always include base directory and include the sub directory items as well.
                    DirectoryProcessingOptions subDirectoryProcessingOptions = DirectoryProcessingOptions.IncludeBaseDirectory | DirectoryProcessingOptions.IncludeSubDirectoryItems;

                    zipDirectory(subDir, zipArchive, entryPrefix + subDirectoryName, compressionLevel, subDirectoryProcessingOptions, cancellationToken);
                }
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
            // Delete any left over file in the target location.
            FileInfo fileInfo = new FileInfo(targetPath);
            if (fileInfo.Exists)
            {
                fileInfo.Delete();
            }
        }


        // private instance fields

        private string sourcePath;
        private string targetPath;
        private bool isFolderPath;
        private bool overwriteTargetIfExists;
        private DirectoryProcessingOptions directoryProcessingOptions;
        private CancellationTokenSource cancellationTokenSource;
        private Action<object> progressUpdater;
        private ZipStatus zipStatus;
        private Exception exception;
        private int totalItems;
        private int progress;
        private string itemPath;
    }
}
