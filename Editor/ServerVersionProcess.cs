using UnityEditor;
using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

// Processes the backup and restore commands of the local Docker or WSL Server ///

namespace NorthernRogue.CCCP.Editor {

public class ServerVersionProcess
{
    private ServerWSLProcess wslProcess;
    private ServerDockerProcess dockerProcess;
    private bool debugMode;
    private Action<string, int> logCallback;
    
    // Server control delegates
    private Func<bool> isServerRunningDelegate;
    private Action startServerDelegate;
    private Action stopServerDelegate;

    public ServerVersionProcess(ServerWSLProcess wslProcess, Action<string, int> logCallback, bool debugMode = false)
    {
        this.wslProcess = wslProcess;
        this.logCallback = logCallback;
        this.debugMode = debugMode;
    }
    
    /// <summary>
    /// Constructor for Docker-based server version processes
    /// </summary>
    public ServerVersionProcess(ServerDockerProcess dockerProcess, Action<string, int> logCallback, bool debugMode = false)
    {
        this.dockerProcess = dockerProcess;
        this.logCallback = logCallback;
        this.debugMode = debugMode;
    }
    
    /// <summary>
    /// Configure the server control delegates after ServerWindow is fully initialized
    /// </summary>
    public void ConfigureServerControlDelegates(
        Func<bool> isServerRunningDelegate,
        Action startServerDelegate,
        Action stopServerDelegate)
    {
        this.isServerRunningDelegate = isServerRunningDelegate;
        this.startServerDelegate = startServerDelegate;
        this.stopServerDelegate = stopServerDelegate;
    }

    #region Backup Server

    public async void BackupServerDataWSL(string backupDirectory, string userName)
    {
        if (string.IsNullOrEmpty(backupDirectory))
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        string wslBackupPath = wslProcess.GetWslPath(backupDirectory);
        string spacetimePath = $"/home/{userName}/.local/share/spacetime/data";
        
        // Ensure the converted path is valid
        if (string.IsNullOrEmpty(backupDirectory) || wslBackupPath == "~")
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        // Construct the backup command with timestamp
        string backupCommand = $"tar czf \"{wslBackupPath}/spacetimedb_wsl_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimePath}";
        var result = await wslProcess.RunServerCommandAsync(backupCommand);
        
        if (result.success && debugMode)
            logCallback("Server backup created successfully.", 1);
        else
            logCallback($"Backup may have failed: {result.error}", -1);
    }

    public async void BackupServerDataDocker(string backupDirectory)
    {
        if (string.IsNullOrEmpty(backupDirectory))
        {
            logCallback("Error: Backup directory is not set or invalid.", -1);
            return;
        }

        if (!Directory.Exists(backupDirectory))
        {
            logCallback("Error: Backup directory does not exist.", -1);
            return;
        }

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string backupFileName = $"spacetimedb_docker_backup_{timestamp}.tar.gz";
        string containerTempBackup = $"/tmp/{backupFileName}";
        string hostBackupPath = Path.Combine(backupDirectory, backupFileName);

        try
        {
            logCallback("Creating backup of Docker container data...", 0);

            // First check if data directory exists and has content
            string checkCommand = "[ -d /home/spacetime/.local/share/spacetime/data ] && [ \"$(ls -A /home/spacetime/.local/share/spacetime/data)\" ] && echo 'exists' || echo 'empty'";
            var checkResult = await dockerProcess.RunServerCommandAsync(checkCommand);
            
            if (checkResult.output != null && checkResult.output.Trim() == "empty")
            {
                logCallback("Warning: SpacetimeDB data directory is empty. No backup created.", -2);
                return;
            }

            // Create tar.gz backup inside the container - archive the data directory from its parent
            string tarCommand = $"tar czf {containerTempBackup} -C /home/spacetime/.local/share/spacetime data";
            var tarResult = await dockerProcess.RunServerCommandAsync(tarCommand);

            if (!tarResult.success)
            {
                logCallback($"Failed to create backup inside container: {tarResult.error}", -1);
                return;
            }

            // Copy backup file from container to host using cross-platform approach
            string dockerCpCommand = $"docker cp {ServerDockerProcess.ContainerName}:{containerTempBackup} \"{hostBackupPath}\"";
            
            Process cpProcess = new Process();
            cpProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            cpProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(dockerCpCommand);
            cpProcess.StartInfo.UseShellExecute = false;
            cpProcess.StartInfo.CreateNoWindow = true;
            cpProcess.StartInfo.RedirectStandardOutput = true;
            cpProcess.StartInfo.RedirectStandardError = true;
            
            // Ensure PATH is properly set on macOS where GUI apps don't inherit shell PATH
            ServerUtilityProvider.SetEnhancedPATHForProcess(cpProcess.StartInfo);
            
            cpProcess.Start();
            string cpOutput = await cpProcess.StandardOutput.ReadToEndAsync();
            string cpError = await cpProcess.StandardError.ReadToEndAsync();
            cpProcess.WaitForExit();

            if (cpProcess.ExitCode != 0)
            {
                logCallback($"Failed to copy backup from container: {cpError}", -1);
                return;
            }

            // Clean up temp backup file in container
            string cleanupCommand = $"rm {containerTempBackup}";
            await dockerProcess.RunServerCommandAsync(cleanupCommand);

            if (debugMode)
                logCallback($"Docker server backup created successfully: {hostBackupPath}", 1);
            else
                logCallback("Server backup created successfully.", 1);
        }
        catch (Exception ex)
        {
            logCallback($"Error during Docker backup: {ex.Message}", -1);
        }
    }

    #endregion

    #region Clear Server

    public async void ClearServerDataDocker()
    {
        // Check if server is running
        bool wasRunning = ServerIsRunning();
        
        if (wasRunning)
        {
            logCallback("Stopping server to safely clear data...", 0);
            StopServer();
            // Docker container needs some time to stop
            await Task.Delay(15000);
        }

        try
        {
            logCallback("Clearing Docker volume data...", 0);
            
            // Use a temporary alpine container to clear the volume
            // Mount the volume at /data in the temp container
            string clearCommand = $"docker run --rm -v spacetimedb-data:/data alpine sh -c \"rm -rf /data/*\"";
            
            Process clearProcess = new Process();
            clearProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            clearProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(clearCommand);
            clearProcess.StartInfo.UseShellExecute = false;
            clearProcess.StartInfo.CreateNoWindow = true;
            clearProcess.StartInfo.RedirectStandardOutput = true;
            clearProcess.StartInfo.RedirectStandardError = true;
            
            // Ensure PATH is properly set on macOS where GUI apps don't inherit shell PATH
            ServerUtilityProvider.SetEnhancedPATHForProcess(clearProcess.StartInfo);
            
            clearProcess.Start();
            string output = await clearProcess.StandardOutput.ReadToEndAsync();
            string error = await clearProcess.StandardError.ReadToEndAsync();
            clearProcess.WaitForExit();
            
            if (clearProcess.ExitCode == 0)
            {
                logCallback("Docker server data cleared successfully.", 1);
            }
            else
            {
                logCallback($"Clear operation failed: {error}", -1);
            }
        }
        finally
        {
            if (wasRunning)
            {
                logCallback("Restarting server...", 0);
                await Task.Delay(1000);
                StartServer();
            }
        }
    }

    public async void ClearServerDataWSL(string userName)
    {
        if (string.IsNullOrEmpty(userName))
        {
            logCallback("Error: Username is not set or invalid.", -1);
            return;
        }

        // Check if server is running
        bool wasRunning = ServerIsRunning();
        
        if (wasRunning)
        {
            logCallback("Stopping server to safely clear data...", 0);
            StopServer();
            // WSL needs less time to stop
            await Task.Delay(3000);
        }

        string spacetimePath = $"/home/{userName}/.local/share/spacetime/data";
        
        // Construct the clear command to remove all files in the data directory
        string clearCommand = $"rm -rf {spacetimePath}/*";
        
        logCallback($"Clearing server data from {spacetimePath}...", 0);
        var result = await wslProcess.RunServerCommandAsync(clearCommand);
        
        if (result.success)
            logCallback("Server data cleared successfully.", 1);
        else
            logCallback($"Clear operation may have failed: {result.error}", -1);
    }

    #endregion

    #region Restore Server

    public async void RestoreServerDataDocker(string backupDirectory, string backupFilePath = null)
    {
        // Ensure the backup directory is valid
        if (string.IsNullOrEmpty(backupDirectory) || !Directory.Exists(backupDirectory))
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

        // Display confirmation dialog due to overwrite risk
        if (EditorUtility.DisplayDialog(
            "Confirm Restore",
            "This will extract your backup and restore the data, replacing all current data in the Docker container.\n\nAre you sure you want to continue?",
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
                    await CreatePreRestoreBackupDocker(backupDirectory);
                });
            }
            
            // Check if server is running and handle server stop/start
            bool wasRunning = ServerIsRunning();
            
            if (wasRunning)
            {
                logCallback("Stopping server to safely restore data...", 0);
                StopServer();
                
                // Docker container needs some time to stop
                await Task.Delay(15000);
            }
            
            try
            {
                await PerformRestoreDocker(backupFilePath);
                
                if (wasRunning)
                {
                    logCallback("Restarting server...", 0);
                    await Task.Delay(1000);
                    StartServer();
                }
            }
            catch (Exception ex)
            {
                logCallback($"Restore failed: {ex.Message}", -1);
                if (debugMode) logCallback($"Stack trace: {ex.StackTrace}", -1);
            }
        }
        else
        {
            logCallback("Restore canceled by user.", 0);
        }
    }

    private async Task CreatePreRestoreBackupDocker(string backupDirectory)
    {
        logCallback("Creating backup of current data before restoring...", 0);
        
        if (string.IsNullOrEmpty(backupDirectory) || !Directory.Exists(backupDirectory))
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
        
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string backupFileName = $"spacetimedb_docker_pre_restore_backup_{timestamp}.tar.gz";
        string containerTempBackup = $"/tmp/{backupFileName}";
        string hostBackupPath = Path.Combine(backupDirectory, backupFileName);

        // Create backup inside container
        string tarCommand = $"tar czf {containerTempBackup} -C /home/spacetime/.local/share/spacetime data";
        var tarResult = await dockerProcess.RunServerCommandAsync(tarCommand);
        
        if (!tarResult.success)
        {
            logCallback($"Pre-restore backup may have failed: {tarResult.error}", -2);
            return;
        }

        // Copy backup to host using cross-platform approach
        string dockerCpCommand = $"docker cp {ServerDockerProcess.ContainerName}:{containerTempBackup} \"{hostBackupPath}\"";
        
        Process cpProcess = new Process();
        cpProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
        cpProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(dockerCpCommand);
        cpProcess.StartInfo.UseShellExecute = false;
        cpProcess.StartInfo.CreateNoWindow = true;
        cpProcess.StartInfo.RedirectStandardOutput = true;
        cpProcess.StartInfo.RedirectStandardError = true;
        
        // Ensure PATH is properly set on macOS where GUI apps don't inherit shell PATH
        ServerUtilityProvider.SetEnhancedPATHForProcess(cpProcess.StartInfo);
        
        cpProcess.Start();
        cpProcess.WaitForExit();

        if (cpProcess.ExitCode == 0)
        {
            logCallback("Pre-restore backup created successfully in your backup directory.", 1);
            
            // Clean up temp file in container
            await dockerProcess.RunServerCommandAsync($"rm {containerTempBackup}");
        }
        else
        {
            logCallback("Pre-restore backup may have failed.", -2);
        }
    }

    private async Task PerformRestoreDocker(string backupFilePath)
    {
        logCallback("Starting Docker restore process...", 0);

        try
        {
            // Get absolute path for the backup file
            string absoluteBackupPath = Path.GetFullPath(backupFilePath);
            string backupFileName = Path.GetFileName(absoluteBackupPath);
            string backupDirectory = Path.GetDirectoryName(absoluteBackupPath);
            
            logCallback("Restoring data to Docker volume...", 0);
            
            // Use a temporary alpine container to:
            // 1. Mount the volume at /data
            // 2. Mount the backup directory at /backup (read-only)
            // 3. Clear existing data and extract the backup
            string restoreCommand = $"docker run --rm " +
                $"-v spacetimedb-data:/data " +
                $"-v \"{backupDirectory}:/backup:ro\" " +
                $"alpine sh -c \"rm -rf /data/* && tar xzf /backup/{backupFileName} -C /data --strip-components=1\"";
            
            Process restoreProcess = new Process();
            restoreProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
            restoreProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(restoreCommand);
            restoreProcess.StartInfo.UseShellExecute = false;
            restoreProcess.StartInfo.CreateNoWindow = true;
            restoreProcess.StartInfo.RedirectStandardOutput = true;
            restoreProcess.StartInfo.RedirectStandardError = true;
            
            // Ensure PATH is properly set on macOS where GUI apps don't inherit shell PATH
            ServerUtilityProvider.SetEnhancedPATHForProcess(restoreProcess.StartInfo);
            
            restoreProcess.Start();
            string output = await restoreProcess.StandardOutput.ReadToEndAsync();
            string error = await restoreProcess.StandardError.ReadToEndAsync();
            restoreProcess.WaitForExit();

            if (restoreProcess.ExitCode != 0)
            {
                logCallback($"Failed to restore backup: {error}", -1);
                return;
            }

            logCallback("Docker restore completed successfully!", 1);
            
            EditorUtility.DisplayDialog(
                "Restore Completed",
                "SpacetimeDB data has been successfully restored from backup to the Docker volume.\n\n" +
                "The server will use this data when it starts.",
                "OK"
            );
        }
        catch (Exception ex)
        {
            logCallback($"Error during Docker restore: {ex.Message}", -1);
            throw;
        }
    }

    public async void RestoreServerDataWSL(string backupDirectory, string userName, string backupFilePath = null)
    {
        string wslBackupPath = wslProcess.GetWslPath(backupDirectory);
        
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
        string wslBackupFilePath = wslProcess.GetWslPath(backupFilePath);
        
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
                        await CreatePreRestoreBackupWSL(backupDirectory, userName);
                    } catch (Exception ex) {
                        // Catch and log exceptions but allow process to continue
                        logCallback($"Pre-restore backup failed: {ex.Message}", -2);
                    }
                });
            }
            
            // Check if server is running and handle server stop/start
            bool wasRunning = ServerIsRunning();
            
            // Get the current autoCloseWsl setting
            if (wasRunning)
            {
                StopServer();
                
                // Small delay to ensure server has stopped
                System.Threading.Thread.Sleep(2000);
            }
            
            try
            {
                PerformRestoreWSL(backupFilePath, userName);
                
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

    private async Task CreatePreRestoreBackupWSL(string backupDirectory, string userName)
    {
        logCallback("Creating backup of current data before restoring...", 0);
        
        string wslBackupPath = wslProcess.GetWslPath(backupDirectory);
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
        string backupCommand = $"tar czf \"{wslBackupPath}/spacetimedb_wsl_pre_restore_backup_$(date +%F_%H-%M-%S).tar.gz\" {spacetimeDataPath}";
        var result = await wslProcess.RunServerCommandAsync(backupCommand);
        
        if (result.success)
            logCallback("Pre-restore backup created successfully in your backup directory.", 1);
        else
            logCallback($"Backup may have failed: {result.error}", -2);
    }
    #endregion

    #region Restore Helper Methods
    private void PerformRestoreWSL(string backupFilePath, string userName)
    {
        logCallback("Starting automated file restore...", 0);
        
        // Create platform-specific temp directory for extraction
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
            
            // Open file explorer windows based on platform
            if (ServerUtilityProvider.IsWindows())
            {
                Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
                Process.Start("explorer.exe", $"\"{wslPath}\"");
            }
            else if (ServerUtilityProvider.IsMacOS())
            {
                Process.Start("open", extractedFolderToOpen);
                // Can't easily open WSL path on macOS, just show the extracted folder
            }
            else if (ServerUtilityProvider.IsLinux())
            {
                Process.Start("xdg-open", extractedFolderToOpen);
            }
            
            EditorUtility.DisplayDialog(
                "Manual Restore Required",
                "The data directory could not be found automatically in the extracted backup.\n\n" +
                "A file manager window has been opened to the extracted backup files.\n\n" +
                "Please manually find the 'data' folder in the backup and copy it to replace the one in the WSL SpacetimeDB directory.",
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
            
            // Open file explorer windows based on platform
            if (ServerUtilityProvider.IsWindows())
            {
                Process.Start("explorer.exe", $"\"{extractedFolderToOpen}\"");
                Process.Start("explorer.exe", $"\"{wslPath}\"");
            }
            else if (ServerUtilityProvider.IsMacOS())
            {
                Process.Start("open", extractedFolderToOpen);
            }
            else if (ServerUtilityProvider.IsLinux())
            {
                Process.Start("xdg-open", extractedFolderToOpen);
            }
            
            EditorUtility.DisplayDialog(
                "Automated Restore Failed",
                $"Error: {ex.Message}\n\n" +
                "A file manager window has been opened to the extracted backup files.\n\n" +
                "Please manually copy the 'data' folder to complete the restore.",
                "OK"
            );
        }
    }

    private void ExtractBackup(string backupFilePath, string windowsTempPath)
    {
        logCallback("Extracting backup archive... (this may take a moment)", 0);
        
        try
        {
            if (ServerUtilityProvider.IsWindows())
            {
                // Windows: Use batch file approach
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
                try
                {
                    File.Delete(batchFilePath);
                }
                catch
                {
                    // Ignore errors when deleting the batch file
                }
                
                if (!string.IsNullOrEmpty(extractOutput))
                    logCallback("Extraction output: " + extractOutput, 0);
                if (!string.IsNullOrEmpty(extractError))
                    logCallback("Extraction errors: " + extractError, -1);
            }
            else
            {
                // macOS/Linux: Use tar command directly through shell
                string tarCommand = $"tar -xf \"{backupFilePath}\" -C \"{windowsTempPath}\"";
                
                Process extractProcess = new Process();
                extractProcess.StartInfo.FileName = ServerUtilityProvider.GetShellExecutable();
                extractProcess.StartInfo.Arguments = ServerUtilityProvider.GetShellArguments(tarCommand);
                extractProcess.StartInfo.UseShellExecute = false;
                extractProcess.StartInfo.CreateNoWindow = true;
                extractProcess.StartInfo.RedirectStandardOutput = true;
                extractProcess.StartInfo.RedirectStandardError = true;
                
                // Ensure PATH is properly set on macOS
                ServerUtilityProvider.SetEnhancedPATHForProcess(extractProcess.StartInfo);
                
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
                
                if (extractProcess.ExitCode != 0)
                {
                    if (!string.IsNullOrEmpty(extractError))
                        logCallback("Extraction error: " + extractError, -1);
                    throw new Exception($"Tar extraction failed with exit code {extractProcess.ExitCode}");
                }
                
                if (!string.IsNullOrEmpty(extractOutput))
                    logCallback("Extraction output: " + extractOutput, 0);
            }
            
            logCallback("Extraction completed.", 1);
        }
        catch (Exception ex)
        {
            logCallback($"Error during extraction: {ex.Message}", -1);
            throw;
        }
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
        // If delegate is configured, use it
        if (stopServerDelegate != null)
            stopServerDelegate();
    }
    
    private void StartServer()
    {       
        // If delegate is configured, use it
        if (startServerDelegate != null)
            startServerDelegate();
    }

    #endregion
} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games