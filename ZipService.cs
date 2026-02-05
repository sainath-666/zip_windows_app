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

            RaiseStatusChanged("Scanning folder structure...");
            RaiseLogMessage($"Starting scan of: {rootFolderPath}");

            // Create a temporary directory (much smaller - only for intermediate zips)
            string tempDir = Path.Combine(Path.GetTempPath(), "BottomUpZip_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Get all subdirectories from SOURCE (no copying)
                RaiseStatusChanged("Analyzing folder structure...");
                List<DirectoryInfo> allDirectories = GetAllSubdirectoriesRecursive(rootFolderPath);

                // Sort by depth (deepest first)
                var sortedDirectories = allDirectories
                    .OrderByDescending(d => GetFolderDepth(d.FullName, rootFolderPath))
                    .ToList();

                RaiseLogMessage($"Found {sortedDirectories.Count} subdirectories to process");

                // Create a mapping of source paths to their temp zip paths
                Dictionary<string, string> folderToZipMap = new Dictionary<string, string>();

                // Process each directory from deepest to shallowest
                int totalFolders = sortedDirectories.Count + 1; // +1 for root
                int processedFolders = 0;

                RaiseStatusChanged("Creating bottom-up zip structure...");

                foreach (var directory in sortedDirectories)
                {
                    try
                    {
                        if (!HasManifestFiles(directory.FullName))
                        {
                            // Even if we skip zipping, we count it as processed so the progress bar is accurate
                            processedFolders++;
                            RaiseProgressChanged(processedFolders, totalFolders);
                            continue;
                        }

                        string folderName = directory.Name;
                        string tempZipPath = Path.Combine(tempDir, Guid.NewGuid().ToString() + ".zip");

                        RaiseOperationChanged($"Zipping: {directory.Name}");
                        RaiseLogMessage($"Processing: {directory.Name}");

                        // Zip this folder directly from source, including any child zips we created
                        ZipFolderWithChildZips(directory.FullName, tempZipPath, folderToZipMap);

                        // Map this folder to its zip
                        folderToZipMap[directory.FullName] = tempZipPath;

                        processedFolders++;
                        RaiseProgressChanged(processedFolders, totalFolders);

                        RaiseLogMessage($"✓ Completed: {folderName}");
                    }
                    catch (Exception ex)
                    {
                        RaiseLogMessage($"✗ Error processing {directory.FullName}: {ex.Message}");
                    }
                }

                // Finally create the root zip with all child zips
                RaiseOperationChanged($"Creating final zip file...");
                RaiseLogMessage($"Creating final output: {outputZipPath}");

                if (File.Exists(outputZipPath))
                {
                    File.Delete(outputZipPath);
                }

                ZipFolderWithChildZips(rootFolderPath, outputZipPath, folderToZipMap);
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
        /// Checks if the folder contains the required manifest files
        /// </summary>
        private bool HasManifestFiles(string folderPath)
        {
            try
            {
                var files = Directory.GetFiles(folderPath)
                                   .Select(Path.GetFileName)
                                   .Where(f => f != null)
                                   .Select(f => f!.ToLower())
                                   .ToHashSet();

                bool hasInit = files.Contains("__init__.py") || files.Contains("_init.py");
                bool hasManifest = files.Contains("__manifest__.py") || files.Contains("__manifest_.py");

                return hasInit && hasManifest;
            }
            catch
            {
                return false;
            }
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
        /// Zips a folder including files and child zips from the mapping
        /// </summary>
        private void ZipFolderWithChildZips(string sourceFolderPath, string zipFilePath, Dictionary<string, string> folderToZipMap)
        {
            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            using (FileStream zipStream = new FileStream(zipFilePath, FileMode.Create))
            using (ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                // Add all files in this directory
                foreach (string filePath in Directory.GetFiles(sourceFolderPath))
                {
                    string fileName = Path.GetFileName(filePath);
                    archive.CreateEntryFromFile(filePath, fileName, CompressionLevel.Optimal);
                }

                // Add child folders as zip files (if they were already zipped)
                foreach (string subDirPath in Directory.GetDirectories(sourceFolderPath))
                {
                    string subDirName = Path.GetFileName(subDirPath);
                    
                    // Check if we already zipped this subfolder
                    if (folderToZipMap.ContainsKey(subDirPath))
                    {
                        // Add the zip file instead of the folder
                        string childZipPath = folderToZipMap[subDirPath];
                        if (File.Exists(childZipPath))
                        {
                            archive.CreateEntryFromFile(childZipPath, $"{subDirName}.zip", CompressionLevel.Optimal);
                        }
                    }
                    else
                    {
                        // This subfolder wasn't processed (manifest missing)
                        // Add files from this subdirectory recursively, checking for nested zips down the line
                        AddDirectoryToArchive(archive, subDirPath, subDirName, folderToZipMap);
                    }
                }
            }
        }

        /// <summary>
        /// Recursively adds a directory and its contents to a zip archive, respecting nested zips in the map
        /// </summary>
        private void AddDirectoryToArchive(ZipArchive archive, string sourcePath, string entryPrefix, Dictionary<string, string> folderToZipMap)
        {
            // Add all files
            foreach (string filePath in Directory.GetFiles(sourcePath))
            {
                string fileName = Path.GetFileName(filePath);
                string entryName = Path.Combine(entryPrefix, fileName).Replace('\\', '/');
                archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
            }

            // Add all subdirectories
            foreach (string subDirPath in Directory.GetDirectories(sourcePath))
            {
                string subDirName = Path.GetFileName(subDirPath);
                
                // Check if this subfolder is a pre-zipped module
                if (folderToZipMap.TryGetValue(subDirPath, out string? childZipPath) && File.Exists(childZipPath))
                {
                    // Add the zip file
                    string entryName = Path.Combine(entryPrefix, subDirName + ".zip").Replace('\\', '/');
                    archive.CreateEntryFromFile(childZipPath, entryName, CompressionLevel.Optimal);
                }
                else
                {
                    // Recurse as logical folder
                    string newPrefix = Path.Combine(entryPrefix, subDirName);
                    AddDirectoryToArchive(archive, subDirPath, newPrefix, folderToZipMap);
                }
            }
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
