using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BottomUpZipper
{
    /// <summary>
    /// Service class that handles the bottom-up folder zipping logic
    /// </summary>
    public class ZipService
    {
        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? OperationChanged;
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// Zips a folder and all its subfolders in a bottom-up manner
        /// </summary>
        /// <param name="rootFolderPath">The root folder to start zipping from</param>
        /// <param name="zipRootFolder">Whether to zip the root folder itself</param>
        public void ZipFolderBottomUp(string rootFolderPath, bool zipRootFolder)
        {
            if (string.IsNullOrEmpty(rootFolderPath) || !Directory.Exists(rootFolderPath))
            {
                throw new ArgumentException("Invalid folder path", nameof(rootFolderPath));
            }

            RaiseStatusChanged("Scanning folders...");
            RaiseLogMessage($"Starting scan of: {rootFolderPath}");

            // Get all subdirectories recursively
            List<DirectoryInfo> allDirectories = GetAllSubdirectoriesRecursive(rootFolderPath);

            // Sort by depth (deepest first)
            var sortedDirectories = allDirectories
                .OrderByDescending(d => GetFolderDepth(d.FullName, rootFolderPath))
                .ToList();

            RaiseLogMessage($"Found {sortedDirectories.Count} subdirectories to process");

            // Process each directory from deepest to shallowest
            int totalFolders = sortedDirectories.Count;
            int processedFolders = 0;

            foreach (var directory in sortedDirectories)
            {
                try
                {
                    // Skip if directory no longer exists (might have been deleted as part of parent)
                    if (!directory.Exists)
                    {
                        continue;
                    }

                    string folderName = directory.Name;
                    string parentPath = directory.Parent?.FullName ?? string.Empty;
                    string zipFilePath = Path.Combine(parentPath, $"{folderName}.zip");

                    RaiseOperationChanged($"Zipping: {directory.FullName}");
                    RaiseLogMessage($"Processing: {directory.FullName}");

                    // Zip the folder
                    ZipFolder(directory.FullName, zipFilePath);

                    // Delete the original folder after successful zipping
                    DeleteFolder(directory.FullName);

                    processedFolders++;
                    RaiseProgressChanged(processedFolders, totalFolders);

                    RaiseLogMessage($"✓ Completed: {folderName}.zip");
                }
                catch (Exception ex)
                {
                    RaiseLogMessage($"✗ Error processing {directory.FullName}: {ex.Message}");
                    // Continue with other folders even if one fails
                }
            }

            // Optionally zip the root folder itself
            if (zipRootFolder)
            {
                try
                {
                    DirectoryInfo rootDir = new DirectoryInfo(rootFolderPath);
                    string rootParentPath = rootDir.Parent?.FullName ?? Path.GetDirectoryName(rootFolderPath) ?? string.Empty;
                    string rootZipPath = Path.Combine(rootParentPath, $"{rootDir.Name}.zip");

                    RaiseOperationChanged($"Zipping root folder: {rootFolderPath}");
                    RaiseLogMessage($"Zipping root folder: {rootFolderPath}");

                    ZipFolder(rootFolderPath, rootZipPath);
                    DeleteFolder(rootFolderPath);

                    RaiseLogMessage($"✓ Root folder zipped: {rootDir.Name}.zip");
                }
                catch (Exception ex)
                {
                    RaiseLogMessage($"✗ Error zipping root folder: {ex.Message}");
                    throw;
                }
            }

            RaiseStatusChanged("All folders processed successfully!");
            RaiseOperationChanged("");
        }

        /// <summary>
        /// Gets all subdirectories recursively
        /// </summary>
        private List<DirectoryInfo> GetAllSubdirectoriesRecursive(string rootPath)
        {
            List<DirectoryInfo> directories = new List<DirectoryInfo>();
            DirectoryInfo rootDir = new DirectoryInfo(rootPath);

            try
            {
                // Get immediate subdirectories
                DirectoryInfo[] subDirs = rootDir.GetDirectories();

                foreach (var subDir in subDirs)
                {
                    try
                    {
                        // Add this directory
                        directories.Add(subDir);

                        // Recursively get subdirectories
                        directories.AddRange(GetAllSubdirectoriesRecursive(subDir.FullName));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        RaiseLogMessage($"⚠ Access denied: {subDir.FullName}");
                    }
                    catch (Exception ex)
                    {
                        RaiseLogMessage($"⚠ Error scanning {subDir.FullName}: {ex.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                RaiseLogMessage($"⚠ Access denied: {rootPath}");
            }

            return directories;
        }

        /// <summary>
        /// Calculates the depth of a folder relative to the root
        /// </summary>
        private int GetFolderDepth(string folderPath, string rootPath)
        {
            // Normalize paths
            folderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
            rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);

            // Remove root path from folder path
            string relativePath = folderPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar);

            // Count directory separators
            if (string.IsNullOrEmpty(relativePath))
            {
                return 0;
            }

            return relativePath.Split(Path.DirectorySeparatorChar).Length;
        }

        /// <summary>
        /// Zips a folder to a specified zip file path
        /// </summary>
        private void ZipFolder(string sourceFolderPath, string zipFilePath)
        {
            // Delete existing zip file if it exists
            if (File.Exists(zipFilePath))
            {
                RaiseLogMessage($"⚠ Overwriting existing zip: {zipFilePath}");
                File.Delete(zipFilePath);
            }

            // Create the zip file
            ZipFile.CreateFromDirectory(
                sourceFolderPath,
                zipFilePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false
            );
        }

        /// <summary>
        /// Deletes a folder and all its contents
        /// </summary>
        private void DeleteFolder(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }

        #region Event Raising Methods

        private void RaiseProgressChanged(int current, int total)
        {
            double percentComplete = total > 0 ? (double)current / total * 100 : 0;
            ProgressChanged?.Invoke(this, new ProgressEventArgs
            {
                Current = current,
                Total = total,
                PercentComplete = percentComplete
            });
        }

        private void RaiseStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void RaiseOperationChanged(string operation)
        {
            OperationChanged?.Invoke(this, operation);
        }

        private void RaiseLogMessage(string message)
        {
            LogMessage?.Invoke(this, message);
        }

        #endregion
    }

    /// <summary>
    /// Event arguments for progress updates
    /// </summary>
    public class ProgressEventArgs : EventArgs
    {
        public int Current { get; set; }
        public int Total { get; set; }
        public double PercentComplete { get; set; }
    }
}
