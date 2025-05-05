using UnityEngine;
using System.Diagnostics;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace NorthernRogue.CCCP.Editor {

public class ServerCMDProcess
{
    // Settings
    public bool debugMode = false;
    private string userName = "";
    private const string PrefsKeyPrefix = "ServerWindow_";
    
    // Port constants
    public const int SpacetimePort = 3000;
    
    // Path constants for WSL
    public const string WslPidPath = "/tmp/spacetime.pid";
    
    // Async Port Check State
    private volatile bool lastPortCheckResult = false;
    private volatile bool isPortCheckRunning = false;
    private readonly object statusUpdateLock = new object();
    
    // Reference to the server process
    private Process serverProcess;
    
    // Debug logging delegate for verbose output
    private Action<string, int> logCallback;
    
    public ServerCMDProcess(Action<string, int> logCallback, bool debugMode = false)
    {
        this.logCallback = logCallback;
        this.debugMode = debugMode;
        
        // Load username from EditorPrefs
        this.userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Initialized with username from EditorPrefs: {this.userName}");
    }
    
    // Add method to set username
    public void SetUserName(string userName)
    {
        if (string.IsNullOrEmpty(userName))
        {
            // Only log warning in debug mode
            if (debugMode) UnityEngine.Debug.LogWarning("[ServerCMDProcess] Empty username provided. Please set a Debian username in the Server Window.");
        }
        else
        {
            this.userName = userName;
            if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Debian username set to: {this.userName}");
        }
        
        // Save username to EditorPrefs regardless of whether it's empty or not
        EditorPrefs.SetString(PrefsKeyPrefix + "UserName", this.userName);
    }
    
    #region Installation Methods
    
    public async Task<bool> RunPowerShellInstallCommand(string command, Action<string, int> statusCallback = null, bool visibleProcess = true, bool keepWindowOpenForDebug = false)
    {
        Process process = null;
        try
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Running PowerShell command: {command} | Visible: {visibleProcess} | KeepOpen: {keepWindowOpenForDebug}");
            
            process = new Process();
            
            // Build the final command
            string finalCommand = command;
            
            if (visibleProcess)
            {
                // For visible window, use a simpler approach that avoids nested quote issues
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.UseShellExecute = true; // Must be true to show window
                process.StartInfo.CreateNoWindow = false;
                
                // Use a simple batch technique - write the command to a temp file and execute it
                // This avoids all the quote escaping issues
                string tempBatchFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spacetime_cmd_{DateTime.Now.Ticks}.bat");
                
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempBatchFile))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine("echo Running command...");
                    sw.WriteLine(command);
                    
                    if (keepWindowOpenForDebug)
                    {
                        sw.WriteLine("echo Command finished. Press any key to close this window...");
                        sw.WriteLine("pause > nul");
                    }
                    else
                    {
                        sw.WriteLine("echo Command completed. Window will close in 3 seconds...");
                        sw.WriteLine("timeout /t 3 > nul");
                    }
                }
                
                process.StartInfo.Arguments = $"/C \"{tempBatchFile}\"";
                
                if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Created batch file: {tempBatchFile}");
            }
            else
            {
                // For hidden execution
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.Arguments = $"-Command \"{finalCommand.Replace("\"", "`\"")}\"";
                process.StartInfo.Verb = "runas"; // Run as admin for hidden process
            }
            
            process.Start();
            statusCallback?.Invoke($"Executing: {command}", 0);
            
            if (visibleProcess)
            {
                // For visible process, we can't easily get the exit code
                // Wait for a reasonable amount of time for the command to execute
                await Task.Delay(10000); // Wait 10 seconds minimum
                
                // For visible process, we'll assume success unless proven otherwise
                return true;
            }
            else
            {
                // For hidden process, wait for completion and check exit code
                await Task.Run(() => process.WaitForExit());
                int exitCode = process.ExitCode;
                
                if (exitCode == 0)
                {
                    statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' completed successfully.", 1);
                    return true;
                }
                else
                {
                    statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' failed with exit code {exitCode}.", -1);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            string commandExcerpt = command.Length > 50 ? command.Substring(0, 50) + "..." : command;
            statusCallback?.Invoke($"Error executing command '{commandExcerpt}': {ex.Message}", -1);
            UnityEngine.Debug.LogError($"[ServerCMDProcess] Exception: {ex}");
            return false;
        }
        finally
        {
            if (process != null && !visibleProcess) // Only dispose if it's not a visible process
            {
                process?.Dispose();
            }
        }
    }
    
    #endregion
    
    #region Process Execution Methods
    
    // Add a new helper method to check if username is valid
    private bool ValidateUserName()
    {
        if (string.IsNullOrEmpty(userName))
        {
            logCallback("[ServerCMDProcess] No Debian username set. Please set a valid username in the Server Window.", -1);
            return false;
        }
        return true;
    }

    public Process StartVisibleServerProcess(string serverDirectory)
    {
        // Validate username before proceeding
        if (!ValidateUserName()) return null;

        try {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            
            // Build visible mode command carefully
            string wslPath = GetWslPath(serverDirectory);
            
            // Escape quotes for bash -c "..."
            string innerBashCommand = $"cd \"{wslPath}\" && echo \"=== Starting Spacetime Server (Visible CMD) in $(pwd) ===\" && spacetime start && echo \"=== Server running. Close window to stop ===\"";
            
            // Escape quotes again for cmd.exe /k "..."
            string escapedBashCommand = innerBashCommand.Replace("\"", "\\\""); 
            
            // Use userName instead of hardcoded mchat
            process.StartInfo.Arguments = $"/k wsl -d Debian -u {userName} --exec bash -l -c \"{escapedBashCommand}\"";
            process.StartInfo.UseShellExecute = true;
            
            if (debugMode) logCallback("Starting Spacetime Server (Visible CMD)...", 0);
            process.Start();
            serverProcess = process;
            return process;
        }
        catch (Exception ex) {
            logCallback($"Error starting visible server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public Process StartSilentServerProcess(string logPath)
    {
        // Validate username before proceeding
        if (!ValidateUserName()) return null;

        try {
            // Use absolute path to spacetime with dynamic username
            string spacetimePath = $"/home/{userName}/.local/bin/spacetime";
            string command = $"{spacetimePath} start &>> \"{logPath}\"";
            
            if (debugMode) logCallback($"WSL bash -l -c Command (Absolute Path, No CD): {command}", 0);
            
            Process process = new Process();
            process.StartInfo.FileName = "wsl.exe"; 
            process.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{command}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = false;
            process.StartInfo.RedirectStandardError = false;
            process.EnableRaisingEvents = true; // Don't attempt to change this to StartInfo.EnableRaisingEvents
            
            if (debugMode) logCallback("Attempting to launch server process via bash -l -c...", 0);
            process.Start();
            
            if (debugMode) logCallback($"Server process launched (PID: {process.Id}). Relying on log file & port check...", 0);
            
            serverProcess = process;
            return process;
        }
        catch (Exception ex) {
            logCallback($"Error launching silent server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public void OpenDebianWindow()
    {
        // Validate username before proceeding
        if (!ValidateUserName()) return;

        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/k wsl -d Debian -u {userName} --exec bash -l";
            process.StartInfo.UseShellExecute = true;
            process.Start();
        }
        catch (Exception ex)
        {
            logCallback($"Error opening Debian window: {ex.Message}", -1);
        }
    }
    
    public void ShutdownWsl()
    {
        if (debugMode) logCallback("Attempting to shut down WSL...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c wsl --shutdown";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("WSL shutdown command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to shut down WSL: {ex.Message}", -1);
        }
    }

    public void StartWsl()
    {
        if (debugMode) logCallback("Attempting to start WSL...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c wsl --start";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("WSL startup command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to start WSL: {ex.Message}", -1);
        }
    }
    
    public async Task<bool> CheckPortAsync(int port)
    {
        if (isPortCheckRunning)
            return lastPortCheckResult;
            
        isPortCheckRunning = true;
        
        bool result = await Task.Run(() =>
        {
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = "powershell.exe";
                    process.StartInfo.Arguments = $"-Command \"Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count\"";
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.Start();
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(5000);
                    
                    if (!process.HasExited)
                    {
                        UnityEngine.Debug.LogWarning($"[ServerCMDProcess] PowerShell command timed out for port {port}.");
                        try { process.Kill(); } catch {}
                        return false;
                    }

                    if (int.TryParse(output, out int count) && count > 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                if (debugMode) UnityEngine.Debug.LogError($"[ServerCMDProcess] Error checking port {port}: {ex.Message}");
                return false;
            }
        });
        
        lock (statusUpdateLock)
        {
            lastPortCheckResult = result;
        }
        
        isPortCheckRunning = false;
        return result;
    }
    
    public bool IsPortInUse(int port)
    {
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-Command \"Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (int.TryParse(output, out int count) && count > 0)
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logCallback($"Error checking port {port}: {ex.Message}", -1);
            return false;
        }
    }
    
    public void CheckPrerequisites(Action<bool, bool, bool, bool, bool, bool, bool> callback)
    {
        logCallback("Checking pre-requisites...", 0);
        
        // Get username from EditorPrefs
        string userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (string.IsNullOrEmpty(userName)) {
            userName = "root"; // Fallback to root if no username set
        }
        
        Process process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.Arguments = "-Command \"" +
            "Write-Host 'Checking WSL...'; " +                                                          //hasWSL
            "$wsl = (wsl --status 2>&1); " +                                                            //hasWSL
            "if ($?) { Write-Host 'WSL_INSTALLED=TRUE' } else { Write-Host 'WSL_INSTALLED=FALSE' }; " + //hasWSL
            "Write-Host 'Checking Debian...'; " +                                                                                  //hasDebian
            "wsl --list 2>&1 | Select-String -Pattern 'Debian' -Quiet; " +                                                         //hasDebian
            "if ($?) { Write-Host 'DEBIAN_INSTALLED=TRUE' } else { Write-Host 'DEBIAN_INSTALLED=FALSE' }; " +                    //hasDebian
            "Write-Host 'Checking Debian Trixie...'; " +                                                                           //hasTrixie
            "$trixie = (wsl -d Debian -u "+ userName + " -- cat /etc/os-release 2>&1); " +                                         //hasTrixie
            "if ($trixie -match 'trixie') { Write-Host 'TRIXIE_INSTALLED=TRUE' } else { Write-Host 'TRIXIE_INSTALLED=FALSE' }; " + //hasTrixie
            "Write-Host 'Checking curl...'; " +                                                                                     //hasCurl
            "$curl = (wsl -d Debian -u "+ userName + " -- which curl 2>&1); " +                                                     //hasCurl
            "if ($curl -match '/usr/bin/curl') { Write-Host 'CURL_INSTALLED=TRUE' } else { Write-Host 'CURL_INSTALLED=FALSE' }; " + //hasCurl
            "Write-Host 'Checking SpacetimeDB...'; " +                                                                                          // hasSpacetimeDB
            //"Write-Host 'Using username: " + userName + "'; " +                                                                                 // hasSpacetimeDB
            "$spacetime = (wsl -d Debian -u " + userName + " -- bash -l -c '\"ls -l $HOME/.local/bin\"' 2>&1); " +                                 // hasSpacetimeDB
            //"Write-Host \"SpacetimeDB type result: $spacetime\"; " +                                                                            // hasSpacetimeDB
            "if ($spacetime -match 'spacetime') { Write-Host 'SPACETIMEDB_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDB_INSTALLED=FALSE' }; " + // hasSpacetimeDB
            "Write-Host 'Checking SpacetimeDB...'; " +                                                                                                    // hasSpacetimeDBPath
            //"Write-Host 'Using username: " + userName + "'; " +                                                                                           // hasSpacetimeDBPath
            "$spacetime = (wsl -d Debian -u " + userName + " -- bash -l -c '\"which spacetime\"' 2>&1); " +                                               // hasSpacetimeDBPath
            //"Write-Host \"SpacetimeDB which result: $spacetime\"; " +                                                                                     // hasSpacetimeDBPath
            "if ($spacetime -match 'spacetime') { Write-Host 'SPACETIMEDBPATH_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDBPATH_INSTALLED=FALSE' }; " + // hasSpacetimeDBPath
            "Write-Host 'Checking rustc...'; " +                                                                           //hasRust
            "$rust = (wsl -d Debian -u "+ userName + " -- bash -l -c '\"which rustc\"' 2>&1); " +                          //hasRust
            "if ($rust -match 'rustc') { Write-Host 'RUST_INSTALLED=TRUE' } else { Write-Host 'RUST_INSTALLED=FALSE' }\""; //hasRust
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        
        process.Start();
        
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        
        logCallback(output, 0);
        
        // Parse output
        bool hasWSL = output.Contains("WSL_INSTALLED=TRUE");
        bool hasDebian = output.Contains("DEBIAN_INSTALLED=TRUE");
        bool hasDebianTrixie = output.Contains("TRIXIE_INSTALLED=TRUE");
        bool hasCurl = output.Contains("CURL_INSTALLED=TRUE");
        bool hasSpacetimeDB = output.Contains("SPACETIMEDB_INSTALLED=TRUE");
        bool hasSpacetimeDBPath = output.Contains("SPACETIMEDBPATH_INSTALLED=TRUE");
        bool hasRust = output.Contains("RUST_INSTALLED=TRUE");

        //logCallback($"Pre-requisites check complete. WSL: {hasWSL}, Debian: {hasDebian}, Debian Trixie: {hasDebianTrixie}, curl: {hasCurl}, SpacetimeDB: {hasSpacetimeDB}, SpacetimeDB Path: {hasSpacetimeDBPath}, Rust: {hasRust}", 0);
        if (!hasWSL || !hasDebian || !hasDebianTrixie || !hasCurl || !hasSpacetimeDB || !hasSpacetimeDBPath)
        {
            logCallback("Missing pre-requisites. Install them with the Server Installer Window.", -1);
        } else
        {
            logCallback("Pre-requisites check complete. All required components are installed.", 1);
        }
        callback(hasWSL, hasDebian, hasDebianTrixie, hasCurl, hasSpacetimeDB, hasSpacetimeDBPath, hasRust);
    }
    
    public bool StopServer(string commandPattern = null)
    {
        // Validate username before proceeding
        if (!ValidateUserName()) return false;

        try
        {
            // If commandPattern is null, construct it with current userName
            if (commandPattern == null)
            {
                commandPattern = $"/home/{userName}/.local/bin/spacetime start";
            }
            
            // First try graceful termination
            if (debugMode) logCallback($"Attempting graceful stop (TERM signal) for '{commandPattern}'...", 0);
            int termExitCode = RunWslCommandSilent($"pkill --signal TERM -f \"{commandPattern}\""); 
            if (debugMode) logCallback($"TERM signal sent via pkill -f. Exit Code: {termExitCode} (0=killed, 1=not found)", 0);
            System.Threading.Thread.Sleep(1000); // Wait for graceful shutdown

            // Check if we need force kill
            if (CheckIfServerRunningWsl())
            {
                if (debugMode) logCallback("Server still running after TERM. Sending KILL signal...", -2);
                int killExitCode = RunWslCommandSilent($"pkill --signal KILL -f \"{commandPattern}\"");
                if (debugMode) logCallback($"KILL signal sent via pkill -f. Exit Code: {killExitCode}", 0);
                System.Threading.Thread.Sleep(500);
            }
            else
            {
                if (debugMode) logCallback("Server stopped after TERM signal.", 1);
            }

            // Kill visible process if active
            try 
            {
                if (serverProcess != null && !serverProcess.HasExited)
                {
                    serverProcess.Kill();
                    logCallback("Killed server process.", 0);
                }
            }
            catch (Exception ex) 
            {
                logCallback($"Error killing server process: {ex.Message}", -1);
            }
            
            // Clear process reference
            serverProcess = null;
            
            // Final check
            bool stillRunning = CheckIfServerRunningWsl();
            if (!stillRunning)
            {
                if (debugMode) logCallback("Server stop confirmed via final pgrep check.", 1);
            }
            else
            {
                logCallback("WARNING: Server process might still be running after stop attempts! Check port status.", -1);
            }
            
            return !stillRunning;
        }
        catch (Exception ex)
        {
            logCallback($"Error during server stop sequence: {ex.Message}", -1);
            return false;
        }
    }
    
    public bool CheckIfServerRunningWsl()
    {
        // Check if PID file exists and if process listed in PID is running
        string command = $"test -f {WslPidPath} && ps -p $(cat {WslPidPath}) > /dev/null";
        int exitCode = RunWslCommandSilent(command);
        bool isRunning = exitCode == 0;
        if (debugMode) logCallback($"WSL Check: Server process running? {(isRunning ? "Yes" : "No")}. (Exit code: {exitCode})", 0);
        return isRunning;
    }
    
    public void RemoveStalePidWsl()
    {
        if (debugMode) logCallback($"Attempting to remove stale PID file: {WslPidPath}", 0);
        string command = $"rm -f {WslPidPath}";
        RunWslCommandSilent(command);
    }
    
    public int RunWslCommandSilent(string bashCommand)
    {
        // Validate username before proceeding
        if (!ValidateUserName()) return -1;

        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "wsl.exe";
            string escapedCommand = bashCommand.Replace("\"", "\\\"");
            // Use dynamic userName
            process.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{escapedCommand}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit(5000); // Add a timeout (5 seconds)
            if (!process.HasExited)
            {
                 logCallback($"WSL silent command timed out: {bashCommand}", -1);
                 try { process.Kill(); } catch {} 
                 return -2; // Indicate timeout
            }
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            logCallback($"Error running silent WSL command '{bashCommand}': {ex.Message}", -1);
            return -1; // Indicate failure
        }
    }
    
    public async Task<(string output, string error, bool success)> RunServerCommandAsync(string command, string serverDirectory = null)
    {
        // Validate username before proceeding
        if (!ValidateUserName()) 
            return (string.Empty, "Error: No Debian username set. Please set a valid username in the Server Window.", false);

        try
        {
            string fullCommand;
            // If server directory is specified AND the command is NOT tar, first change to that directory
            if (!string.IsNullOrEmpty(serverDirectory) && !command.Contains("tar"))
            {
                string wslPath = GetWslPath(serverDirectory);
                // Use single quotes inside the bash command for the path
                // The outer double quotes are for the bash -c "..." argument itself
                // Escape inner quotes for the command part
                string escapedCommandPart = command.Replace("\"", "\\\""); 
                fullCommand = $"cd '{wslPath}' && {escapedCommandPart}";
            }
            else
            {
                // Command doesn't need cd, just escape inner quotes for bash -c
                fullCommand = command.Replace("\"", "\\\""); 
            }
            
            Process wslProcess = new Process(); // Renamed for clarity
            wslProcess.StartInfo.FileName = "wsl.exe"; // Execute wsl directly
            // Construct arguments for wsl.exe, using the fully escaped command
            wslProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{fullCommand}\"";
            wslProcess.StartInfo.UseShellExecute = false;
            wslProcess.StartInfo.CreateNoWindow = true;
            wslProcess.StartInfo.RedirectStandardOutput = true; 
            wslProcess.StartInfo.RedirectStandardError = true;  
            
            wslProcess.Start();
            
            // Read streams asynchronously with Task
            var outputTask = wslProcess.StandardOutput.ReadToEndAsync();
            var errorTask = wslProcess.StandardError.ReadToEndAsync();
            
            // Log debug info
            string debug = $"-d Debian -u {userName} --exec bash -l -c \"{fullCommand}\"";
            if (debugMode) logCallback("Debug: " + debug, 0);
            
            // Wait for completion or timeout
            bool exited = await Task.Run(() => wslProcess.WaitForExit(30000)); // 30 seconds timeout
            
            if (!exited)
            {
                logCallback("WSL command timed out after 30 seconds. Attempting to kill process...", -1);
                try { wslProcess.Kill(); } catch (Exception killEx) { logCallback($"Error killing timed-out process: {killEx.Message}", -1); }
            }
            
            // Get the output and error
            string output = await outputTask;
            string error = await errorTask;
            
            // Analyze the result
            bool commandSuccess = false;
            bool isPublishCommand = command.Contains("spacetime publish");
            bool isGenerateCommand = command.Contains("spacetime generate");
            
            if (!string.IsNullOrEmpty(error) && error.Contains("Finished"))
            {
                if (isPublishCommand)
                {
                    logCallback("Successfully published module!", 1);
                    commandSuccess = true;
                }
                else if (isGenerateCommand)
                {
                    logCallback("Successfully generated files!", 1);
                    commandSuccess = true;
                }
                else
                {
                    logCallback("Command finished successfully!", 1);
                    commandSuccess = true;
                }
            }
            else if (error.Contains("tar: Removing leading `/' from member names"))
            {
                logCallback("Successfully backed up SpacetimeDB data!", 1);
                commandSuccess = true;
            }
            else if (error.Contains("command not found") || error.Contains("not found"))
            {
                logCallback($"Error: The command (likely 'spacetime') was not found in the WSL environment for user '{userName}'. Ensure SpacetimeDB is correctly installed and in the PATH for that user.", -1);
                commandSuccess = false;
            }
            
            if (string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error))
            {
                logCallback("Command completed with no output.", 0);
                commandSuccess = true;
            }
            
            return (output, error, commandSuccess);
        }
        catch (Exception ex)
        {
            logCallback($"Error running command: {ex.Message}", -1);
            return (string.Empty, $"Exception: {ex.Message}", false);
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    public string GetWslPath(string windowsPath)
    {
        if (string.IsNullOrEmpty(windowsPath)) return "~";
        string path = windowsPath.Replace("\\", "/").Replace(":", "");
        if (path.Length > 1 && char.IsLetter(path[0]) && path[1] == '/') 
        { path = "/mnt/" + char.ToLower(path[0]) + path.Substring(1); }
        else { path = "~"; }
        return path;
    }
    
    #endregion
} // Class
} // Namespace