using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using NorthernRogue.CCCP.Editor.Settings;

// Processes the backup and restore commands of the local WSL Server ///

namespace NorthernRogue.CCCP.Editor {

public class ServerVersionProcess
{
    private ServerCMDProcess cmdProcessor;
    private bool debugMode;
    private Action<string, int> logCallback;
    
    // Server control delegates
    private Func<bool> isServerRunningDelegate;
    private Func<bool> getAutoCloseWslDelegate;
    private Action<bool> setAutoCloseWslDelegate;
    private Action startServerDelegate;
    private Action stopServerDelegate;

    public ServerVersionProcess(ServerCMDProcess cmdProcessor, Action<string, int> logCallback, bool debugMode = false)
    {
        this.cmdProcessor = cmdProcessor;
        this.logCallback = logCallback;
        this.debugMode = debugMode;
    }
    
    /// <summary>
    /// Configure the server control delegates after ServerWindow is fully initialized
    /// </summary>
    public void ConfigureServerControlDelegates(
        Func<bool> isServerRunningDelegate,
        Func<bool> getAutoCloseWslDelegate,
        Action<bool> setAutoCloseWslDelegate,
        Action startServerDelegate,
        Action stopServerDelegate)
    {
        this.isServerRunningDelegate = isServerRunningDelegate;
        this.getAutoCloseWslDelegate = getAutoCloseWslDelegate;
        this.setAutoCloseWslDelegate = setAutoCloseWslDelegate;
        this.startServerDelegate = startServerDelegate;
        this.stopServerDelegate = stopServerDelegate;
    }

    #region Backup Server Data

    public async void BackupServerData(string backupDirectory, string userName)
    {
        if (string.IsNullOrEmpty(backupDirectory))
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        string wslBackupPath = cmdProcessor.GetWslPath(backupDirectory);
        string spacetimePath = $"/home/{userName}/.local/share/spacetime/data";
        
        // Ensure the converted path is valid
        if (string.IsNullOrEmpty(backupDirectory) || wslBackupPath == "~")
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        // Construct the backup command with timestamp
        string backupCommand = $"tar czf \"{wslBackupPath}/spacetimedb_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimePath}";
        var result = await cmdProcessor.RunServerCommandAsync(backupCommand);
        
        if (result.success)
            logCallback("Server backup created successfully.", 1);
        else
            logCallback($"Backup may have failed: {result.error}", -1);
    }

    #endregion

    #region Clear Server Data

    public async void ClearServerData(string userName)
    {
        if (string.IsNullOrEmpty(userName))
        {
            logCallback("Error: Username is not set or invalid.", -1);
            return;
        }

        string spacetimePath = $"/home/{userName}/.local/share/spacetime/data";
        
        // Construct the clear command to remove all files in the data directory
        string clearCommand = $"rm -rf {spacetimePath}/*";
        
        logCallback($"Clearing server data from {spacetimePath}...", 0);
        var result = await cmdProcessor.RunServerCommandAsync(clearCommand);
        
        if (result.success)
            logCallback("Server data cleared successfully.", 1);
        else
            logCallback($"Clear operation may have failed: {result.error}", -1);
    }

    #endregion

    #region Restore Server Data

    public async void RestoreServerData(string backupDirectory, string userName, string backupFilePath = null)
    {
        string wslBackupPath = cmdProcessor.GetWslPath(backupDirectory);
        
        // Ensure the backup directory is valid
        if (string.IsNullOrEmpty(backupDirectory) || wslBackupPath == "~")
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        // If no backup file specified, show file selection dialog
        if (string.IsNullOrEmpty(backupFilePath))
        {
            backupFilePath = EditorUtility.OpenFilePanel("Select Backup File", backupDirectory, "gz");
            if (string.IsNullOrEmpty(backupFilePath))
            {
                logCallback("Restore canceled: No backup file selected.", 0);
                return;
            }
        }

        // Convert Windows path to WSL path for the selected backup file
        string wslBackupFilePath = cmdProcessor.GetWslPath(backupFilePath);
        
        // Display confirmation dialog due to overwrite risk
        if (EditorUtility.DisplayDialog(
            "Confirm Restore",
            "This will extract your backup and restore the data, replacing all current data.\n\nAre you sure you want to continue?",
            "Yes, Continue",
            "Cancel"))
        {
            // Ask if the user wants to create a backup first
            bool createBackup = EditorUtility.DisplayDialog(
                "Create Backup",
                "Do you want to create a backup of your current data before restoring?\n\n" +
                "This will create a .tar.gz backup file in your backup directory.",
                "Yes, Create Backup",
                "No, Skip Backup");
                
            if (createBackup)
            {
                await Task.Run(async () => {
                    try {
                        await CreatePreRestoreBackupAsync(backupDirectory, userName);
                    } catch (Exception ex) {
                        // Catch and log exceptions but allow process to continue
                        logCallback($"Pre-restore backup failed: {ex.Message}", -2);
                    }
                });
            }
            
            // Check if server is running and handle server stop/start
            bool wasRunning = ServerIsRunning();
            bool autoCloseWslWasTrue = false;
            
            // Get the current autoCloseWsl setting
            if (wasRunning)
            {
                autoCloseWslWasTrue = GetAutoCloseWsl();
                SetAutoCloseWsl(false); // Disable auto close WSL
                StopServer();
                
                // Small delay to ensure server has stopped
                System.Threading.Thread.Sleep(2000);
            }
            
            try
            {
                PerformRestore(backupFilePath, userName);
                
                // Restore the autoCloseWsl setting if it was changed
                if (autoCloseWslWasTrue)
                {
                    SetAutoCloseWsl(true);
                }
                
                // Restart server if it was running
                if (wasRunning)
                {
                    StartServer();
                }
            }
            catch (Exception ex)
            {
                logCallback($"Error during restore process: {ex.Message}", -1);
                if (debugMode) logCallback($"Stack trace: {ex.StackTrace}", -2);
            }
        }
        else
        {
            logCallback("Restore canceled by user.", 0);
        }
    }

    private async Task CreatePreRestoreBackupAsync(string backupDirectory, string userName)
    {
        logCallback("Creating backup of current data before restoring...", 0);
        
        string wslBackupPath = cmdProcessor.GetWslPath(backupDirectory);
        string spacetimeDataPath = $"/home/{userName}/.local/share/spacetime/data";
        
        // Ensure the converted path is valid
        if (string.IsNullOrEmpty(backupDirectory) || wslBackupPath == "~")
        {
            logCallback("Warning: Could not create backup. Backup directory is not set or invalid.", -2);
            
            // Ask if the user wants to continue without backup
            if (!EditorUtility.DisplayDialog(
                "Continue Without Backup?",
                "Could not create a backup because the backup directory is not set or invalid.\n\n" +
                "Do you want to continue with the restore without creating a backup?",
                "Yes, Continue Anyway",
                "No, Cancel Restore"))
            {
                logCallback("Restore canceled by user.", 0);
                throw new Exception("Restore canceled due to backup failure");
            }
            return;
        }
        
        // Create backup with timestamp
        string backupCommand = $"tar czf \"{wslBackupPath}/spacetimedb_pre_restore_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimeDataPath}";
        var result = await cmdProcessor.RunServerCommandAsync(backupCommand);
        
        if (result.success)
            logCallback("Pre-restore backup created successfully in your backup directory.", 1);
        else
            logCallback($"Backup may have failed: {result.error}", -2);
    }

    // Keep the old method for compatibility but mark it as obsolete
    [Obsolete("Use CreatePreRestoreBackupAsync instead")]
    private async void CreatePreRestoreBackup(string backupDirectory, string userName)
    {
        await CreatePreRestoreBackupAsync(backupDirectory, userName);
    }

    private void PerformRestore(string backupFilePath, string userName)
    {
        logCallback("Starting automated file restore...", 0);
        
        // Create Windows temp directory for extraction
        string windowsTempPath = Path.Combine(Path.GetTempPath(), "SpacetimeDBRestore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(windowsTempPath);
        logCallback($"Created temporary directory: {windowsTempPath}", 0);
        
        // Extract the backup
        ExtractBackup(backupFilePath, windowsTempPath);
        
        // Find the spacetime directory within the extracted files
        string expectedSpacetimePath = Path.Combine(windowsTempPath, "home", userName, ".local", "share", "spacetime");
        string extractedFolderToOpen = windowsTempPath;
        
        // Check if the expected path structure exists
        if (Directory.Exists(expectedSpacetimePath))
        {
            extractedFolderToOpen = expectedSpacetimePath;
            logCallback($"Found spacetime directory in extracted backup at: {expectedSpacetimePath}", 1);
        }
        else
        {
            // Try to search for the spacetime directory
            logCallback("Searching for spacetime directory in extracted files...", 0);
            string[] foundDirs = Directory.GetDirectories(windowsTempPath, "spacetime", SearchOption.AllDirectories);
            
            if (foundDirs.Length > 0)
            {
                // Use the first found spacetime directory
                extractedFolderToOpen = foundDirs[0];
                logCallback($"Found spacetime directory at: {extractedFolderToOpen}", 1);
            }
            else
            {
                logCallback("Could not find spacetime directory in extracted files. Falling back to root extraction folder.", 0);
            }
        }
        
        // Define source and destination paths
        string sourceDataDir = Path.Combine(extractedFolderToOpen, "data");
        string wslPath = $@"\\wsl.localhost\Debian\home\{userName}\.local\share\spacetime";
        string destDataDir = Path.Combine(wslPath, "data");
        
        // Verify data directory exists in extracted backup
        if (!Directory.Exists(sourceDataDir))
        {
            logCallback($"Error: Data directory not found in extracted backup at {sourceDataDir}", -1);
            logCallback("Falling back to manual restore method...", 0);
            
            // Open both explorer windows as fallback
            Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
            Process.Start("explorer.exe", $"\"{wslPath}\"");
            
            EditorUtility.DisplayDialog(
                "Manual Restore Required",
                "The data directory could not be found automatically in the extracted backup.\n\n" +
                "Two Explorer windows have been opened:\n" +
                "1. The extracted backup files\n" +
                "2. The WSL SpacetimeDB directory\n\n" +
                "Please manually find the 'data' folder in the backup and copy it to replace the one in the WSL window.",
                "OK"
            );
            return;
        }
        
        try
        {
            // Delete existing data directory if it exists
            if (Directory.Exists(destDataDir))
            {
                logCallback($"Removing existing data directory at {destDataDir}", 0);
                Directory.Delete(destDataDir, true);
            }
            
            // Copy the extracted data directory to the WSL path
            logCallback($"Copying data directory from {sourceDataDir} to {destDataDir}", 0);
            
            // Create the destination directory
            Directory.CreateDirectory(destDataDir);
            
            // Copy files and subdirectories
            CopyDirectory(sourceDataDir, destDataDir);
            
            logCallback("Restore completed successfully!", 1);
            
            // Clean up temporary extraction folder
            try
            {
                logCallback("Cleaning up temporary extraction directory...", 0);
                Directory.Delete(windowsTempPath, true);
                logCallback("Cleanup completed.", 0);
            }
            catch (Exception cleanupEx)
            {
                logCallback($"Warning: Could not clean up temporary extraction directory: {cleanupEx.Message}", -2);
                logCallback($"You may manually delete the directory later: {windowsTempPath}", 0);
            }
            
            EditorUtility.DisplayDialog(
                "Restore Completed",
                "SpacetimeDB data has been successfully restored from backup.",
                "OK"
            );
        }
        catch (Exception ex)
        {
            logCallback($"Error during automated restore: {ex.Message}", -1);
            
            // Fall back to manual restore
            logCallback("Falling back to manual restore method...", 0);
            Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
            Process.Start("explorer.exe", $"\"{wslPath}\"");
            
            EditorUtility.DisplayDialog(
                "Automated Restore Failed",
                $"Error: {ex.Message}\n\n" +
                "Two Explorer windows have been opened for manual restore:\n" +
                "1. The extracted backup files\n" +
                "2. The WSL SpacetimeDB directory\n\n" +
                "Please manually copy the 'data' folder to complete the restore.",
                "OK"
            );
        }
    }

    private void ExtractBackup(string backupFilePath, string windowsTempPath)
    {
        logCallback("Extracting backup archive... (this may take a moment)", 0);
        
        // Write a batch file to execute the extraction
        string batchFilePath = Path.Combine(Path.GetTempPath(), "extract_backup.bat");
        string batchContent = $@"@echo off
        echo Extracting {backupFilePath} to {windowsTempPath}...
        mkdir ""{windowsTempPath}"" 2>nul
        tar -xf ""{backupFilePath}"" -C ""{windowsTempPath}""
        echo Extraction complete
        ";
        
        File.WriteAllText(batchFilePath, batchContent);
        logCallback($"Created extraction batch file: {batchFilePath}", 0);
        
        // Run the batch file
        Process extractProcess = new Process();
        extractProcess.StartInfo.FileName = "cmd.exe";
        extractProcess.StartInfo.Arguments = $"/c \"{batchFilePath}\"";
        extractProcess.StartInfo.UseShellExecute = false;
        extractProcess.StartInfo.CreateNoWindow = true;
        extractProcess.StartInfo.RedirectStandardOutput = true;
        extractProcess.StartInfo.RedirectStandardError = true;
        
        string extractOutput = "";
        string extractError = "";
        
        extractProcess.OutputDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data)) extractOutput += e.Data + "\n";
        };
        extractProcess.ErrorDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data)) extractError += e.Data + "\n";
        };
        
        extractProcess.Start();
        extractProcess.BeginOutputReadLine();
        extractProcess.BeginErrorReadLine();
        extractProcess.WaitForExit();
        
        // Clean up the batch file
        try {
            File.Delete(batchFilePath);
        } catch {
            // Ignore errors when deleting the batch file
        }
        
        if (!string.IsNullOrEmpty(extractOutput))
            logCallback("Extraction output: " + extractOutput, 0);
        if (!string.IsNullOrEmpty(extractError))
            logCallback("Extraction errors: " + extractError, -1);
        
        logCallback("Extraction completed.", 1);
    }

    private void CopyDirectory(string sourceDir, string destDir)
    {
        // Get all files from the source
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }
        
        // Copy subdirectories recursively
        foreach (string directory in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(directory);
            string destSubDir = Path.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectory(directory, destSubDir);
        }
    }

    #endregion

    #region Server Control Methods
    
    private bool ServerIsRunning()
    {
        // If delegate is configured, use it
        return isServerRunningDelegate != null && isServerRunningDelegate();
    }
    
    private void StopServer()
    {
        logCallback("Stopping server before restore...", 0);
        
        // If delegate is configured, use it
        if (stopServerDelegate != null)
            stopServerDelegate();
    }
    
    private void StartServer()
    {
        logCallback("Restarting server after restore...", 0);
        
        // If delegate is configured, use it
        if (startServerDelegate != null)
            startServerDelegate();
    }
    
    private bool GetAutoCloseWsl()
    {
        // If delegate is configured, use it; otherwise fall back to Settings
        if (getAutoCloseWslDelegate != null)
            return getAutoCloseWslDelegate();
            
        return CCCPSettingsAdapter.GetAutoCloseWsl();
    }
    
    private void SetAutoCloseWsl(bool value)
    {
        // If delegate is configured, use it; otherwise set Settings directly
        if (setAutoCloseWslDelegate != null)
            setAutoCloseWslDelegate(value);
        else
            CCCPSettingsAdapter.SetAutoCloseWsl(value);
    }

    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games