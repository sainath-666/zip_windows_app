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
        /// <param name="outputZipPath">The path where the final zip file will be saved</param>
        public void ZipFolderBottomUp(string rootFolderPath, string outputZipPath)
        {
            if (string.IsNullOrEmpty(rootFolderPath) || !Directory.Exists(rootFolderPath))
            {
                throw new ArgumentException("Invalid folder path", nameof(rootFolderPath));
            }

            RaiseStatusChanged("Creating temporary working directory...");
            RaiseLogMessage($"Starting scan of: {rootFolderPath}");

            // Create a temporary directory to build the zip structure
            string tempDir = Path.Combine(Path.GetTempPath(), "BottomUpZip_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Copy the entire folder structure to temp directory
                RaiseStatusChanged("Copying folder structure...");
                string tempRoot = Path.Combine(tempDir, new DirectoryInfo(rootFolderPath).Name);
                CopyDirectory(rootFolderPath, tempRoot);

                // Get all subdirectories recursively from temp directory
                List<DirectoryInfo> allDirectories = GetAllSubdirectoriesRecursive(tempRoot);

                // Sort by depth (deepest first)
                var sortedDirectories = allDirectories
                    .OrderByDescending(d => GetFolderDepth(d.FullName, tempRoot))
                    .ToList();

                RaiseLogMessage($"Found {sortedDirectories.Count} subdirectories to process");

                // Process each directory from deepest to shallowest
                int totalFolders = sortedDirectories.Count + 1; // +1 for root
                int processedFolders = 0;

                foreach (var directory in sortedDirectories)
                {
                    try
                    {
                        // Skip if directory no longer exists
                        if (!directory.Exists)
                        {
                            continue;
                        }

                        string folderName = directory.Name;
                        string parentPath = directory.Parent?.FullName ?? string.Empty;
                        string zipFilePath = Path.Combine(parentPath, $"{folderName}.zip");

                        RaiseOperationChanged($"Zipping: {directory.Name}");
                        RaiseLogMessage($"Processing: {directory.Name}");

                        // Zip the folder
                        ZipFolder(directory.FullName, zipFilePath);

                        // Delete the folder after zipping
                        DeleteFolder(directory.FullName);

                        processedFolders++;
                        RaiseProgressChanged(processedFolders, totalFolders);

                        RaiseLogMessage($"✓ Completed: {folderName}.zip");
                    }
                    catch (Exception ex)
                    {
                        RaiseLogMessage($"✗ Error processing {directory.FullName}: {ex.Message}");
                    }
                }

                // Finally zip the root folder
                RaiseOperationChanged($"Creating final zip file...");
                RaiseLogMessage($"Creating final output: {outputZipPath}");

                // Delete existing output file if it exists
                if (File.Exists(outputZipPath))
                {
                    File.Delete(outputZipPath);
                }

                ZipFolder(tempRoot, outputZipPath);
                processedFolders++;
                RaiseProgressChanged(processedFolders, totalFolders);
                RaiseLogMessage($"✓ Final zip created: {Path.GetFileName(outputZipPath)}");
            }
            finally
            {
                // Clean up temp directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    RaiseLogMessage($"⚠ Could not delete temp directory: {ex.Message}");
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

        /// <summary>
        /// Copies a directory and all its contents recursively
        /// </summary>
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            // Copy all files
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                string destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, true);
            }

            // Copy all subdirectories
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(subDir);
                string destSubDir = Path.Combine(targetDir, dirName);
                CopyDirectory(subDir, destSubDir);
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
