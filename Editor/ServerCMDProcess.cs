using UnityEngine;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

// Runs the local wsl server installation processes and methods ///

namespace NorthernRogue.CCCP.Editor {

public class ServerCMDProcess
{
    // Settings
    public static bool debugMode = false;
    private string userName = "";
    private const string PrefsKeyPrefix = "CCCP_";
    
    // Path constants for WSL
    public const string WslPidPath = "/tmp/spacetime.pid";
    
    // Async Port Check State
    private readonly object statusUpdateLock = new object();
    
    // Server status caching
    private bool cachedServerRunningStatus = false;
    private double lastStatusCacheTime = 0;
    private const double statusCacheTimeout = 10; // Status valid for 10 seconds
    
    // Reference to the server process
    private Process serverProcess;
    
    // Debug logging delegate for verbose output
    private Action<string, int> logCallback;
    
    // Public property to access cached server status
    public bool cachedServerRunningStatus_Public => cachedServerRunningStatus;
    
    public ServerCMDProcess(Action<string, int> logCallback, bool debugMode = false)
    {
        this.logCallback = logCallback;
        ServerCMDProcess.debugMode = debugMode;
        
        // Load username from EditorPrefs
        this.userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Initialized with username from EditorPrefs: {this.userName}");
    }
    
    #region Installation

    public async Task<bool> RunPowerShellInstallCommand(string command, Action<string, int> statusCallback = null, bool visibleProcess = true, bool keepWindowOpenForDebug = false, bool requiresElevation = false)
    {
        Process process = null;
        try
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Running PowerShell command: {command} | Visible: {visibleProcess} | KeepOpen: {keepWindowOpenForDebug} | RequiresElevation: {requiresElevation}");
            
            process = new Process();
            
            if (visibleProcess)
            {
                // For visible window, use a simpler approach that avoids nested quote issues
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.UseShellExecute = true; // Must be true to show window
                process.StartInfo.CreateNoWindow = false;
                
                // Use a simple batch technique - write the command to a temp file and execute it
                string tempBatchFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cosmoscovecontrolpanel_install_{DateTime.Now.Ticks}.bat");
                
                // Construct the batch file content
                // The command itself (e.g., dism.exe) should trigger UAC if needed, as cmd.exe is UseShellExecute=true
                // The batch file should exit with the error code of the command.
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempBatchFile))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine("echo Running command...");
                    sw.WriteLine(command); // The raw command
                    sw.WriteLine($"echo Command finished. Exit code is %ERRORLEVEL%.");
                    sw.WriteLine("if %ERRORLEVEL% neq 0 (");
                    sw.WriteLine("    echo ERROR: Command failed with exit code %ERRORLEVEL%");
                    sw.WriteLine(")");
                    
                    if (keepWindowOpenForDebug)
                    {
                        sw.WriteLine("echo Press any key to close this window...");
                        sw.WriteLine("pause > nul");
                    }
                    else
                    {
                        // Brief pause to see messages, then auto-close.
                        // SpacetimeDB install script itself might pause, so this timeout is mostly for other commands.
                        sw.WriteLine("echo Window will close in 5 seconds if not paused by the command...");
                        sw.WriteLine("timeout /t 5 /nobreak > nul");
                    }
                    sw.WriteLine("exit /b %ERRORLEVEL%"); // Exit batch with command's error level
                }
                
                process.StartInfo.Arguments = $"/C \"{tempBatchFile}\"";
                
                if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Created batch file: {tempBatchFile} with command: {command}");
            }
            else // Hidden execution
            {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.UseShellExecute = false; // Required for redirecting output
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                // process.StartInfo.Verb = "runas"; // This is ineffective with UseShellExecute = false

                string commandToExecute = command;
                if (requiresElevation)
                {
                    // Parse command to get executable and args for Start-Process
                    var (exe, args) = SplitCommandForProcess(commandToExecute);
                    
                    // Escape single quotes for PowerShell string literals
                    string escapedExe = exe.Replace("'", "''");
                    string escapedArgs = args.Replace("'", "''");

                    // $ProgressPreference ensures Start-Process doesn't hang on progress bars for some commands
                    commandToExecute = $"$ProgressPreference = 'SilentlyContinue'; try {{ $process = Start-Process -FilePath '{escapedExe}' -ArgumentList '{escapedArgs}' -Verb RunAs -Wait -PassThru; exit $process.ExitCode; }} catch {{ Write-Error $_; exit 1; }}";
                    if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Elevated command for hidden execution: {commandToExecute}");
                }
                
                // Important: Escape double quotes for the -Command argument string itself
                string escapedFinalCommand = commandToExecute.Replace("\"", "`\"");
                process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{escapedFinalCommand}\"";
            }
            
            process.Start();
            statusCallback?.Invoke($"Executing: {command.Split(' ')[0]}...", 0);
            
            string outputLog = "";
            string errorLog = "";

            if (!visibleProcess)
            {
                // For hidden process, read output/error streams
                outputLog = await process.StandardOutput.ReadToEndAsync();
                errorLog = await process.StandardError.ReadToEndAsync();
            }

            await Task.Run(() => process.WaitForExit()); 
            int exitCode = process.ExitCode;

            if (debugMode && !visibleProcess) {
                if (!string.IsNullOrEmpty(outputLog)) UnityEngine.Debug.Log($"[ServerCMDProcess] Output: {outputLog}");
                if (!string.IsNullOrEmpty(errorLog)) UnityEngine.Debug.LogWarning($"[ServerCMDProcess] Error: {errorLog}");
            }
            
            if (exitCode == 0)
            {
                statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' completed successfully.", 1);
                return true;
            }
            else
            {
                statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' failed with exit code {exitCode}. Check console/window for details.", -1);
                if (!visibleProcess && !string.IsNullOrEmpty(errorLog)) {
                     UnityEngine.Debug.LogError($"[ServerCMDProcess] Hidden command failed. Error stream: {errorLog}");
                } else if (!visibleProcess && !string.IsNullOrEmpty(outputLog)) {
                     UnityEngine.Debug.LogWarning($"[ServerCMDProcess] Hidden command failed. Output stream: {outputLog}");
                }
                return false;
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
            // Dispose hidden process, but leave visible cmd.exe alone as it might still be closing
            if (process != null && !visibleProcess)
            {
                process?.Dispose();
            }
        }
    }
    
    #endregion
    
    #region Process Execution Methods
    
    // Validates that a debian username is configured for WSL operations. Automatically suppresses errors on first run (before prerequisites checked).
    private bool ValidateUserName(bool silentMode, out bool shouldSuppressErrors)
    {
        userName = EditorPrefs.GetString(PrefsKeyPrefix + "UserName", "");
        if (string.IsNullOrEmpty(userName))
        {
            // Auto-enable silent mode on first run (when prerequisites haven't been checked yet)
            bool isFirstRun = !EditorPrefs.GetBool(PrefsKeyPrefix + "wslPrerequisitesChecked", false);
            shouldSuppressErrors = silentMode || isFirstRun;
            
            if (!shouldSuppressErrors)
            {
                logCallback("[ServerCMDProcess] No Debian username set. Please set a valid username in the Server Window.", -1);
            }
            return false;
        }
        shouldSuppressErrors = false;
        return true;
    }

    public Process StartVisibleServerProcess(string serverDirectory)
    {
        // Validate username before proceeding
        if (!ValidateUserName(false, out _)) return null;

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
        if (!ValidateUserName(false, out _)) return null;

        try {
            // Use absolute path to spacetime with dynamic username
            string spacetimePath = $"/home/{userName}/.local/bin/spacetime";
            // Create command that captures PID for better process management
            string command = $"nohup {spacetimePath} start &>> \"{logPath}\" & echo $! > {WslPidPath}";
            
            if (debugMode) logCallback($"WSL bash -l -c Command (with PID capture): {command}", 0);
            
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
            
            if (debugMode) logCallback($"Server process launched (PID: {process.Id}). Creating PID file for WSL process management...", 0);
            
            serverProcess = process;
            return process;
        }
        catch (Exception ex) {
            logCallback($"Error launching silent server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public void OpenDebianWindow(bool userNameReq)
    {
        // Validate username before proceeding
        if (userNameReq)
        {
            if (!ValidateUserName(false, out _)) return;
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
                logCallback($"Error opening Debian window with username {userName}: {ex.Message}", -1);
            }
        } else {
            try
            {
                // Launch Debian directly using wsl.exe like if it was clicked in the start menu
                Process process = new Process();
                process.StartInfo.FileName = "wsl.exe";
                process.StartInfo.Arguments = "-d Debian";
                process.StartInfo.UseShellExecute = true;
                process.Start();
            }
            catch (Exception ex)
            {
                logCallback($"Error opening Debian window: {ex.Message}", -1);
            }
        }
    }
    #endregion

    #region WSL Control
    
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
    #endregion

    #region CheckServerProcess
    public async Task<bool> CheckWslProcessAsync(bool isWslRunning)
    {
        // Validate username before proceeding - silent mode suppresses errors on first run
        if (!ValidateUserName(true, out _)) 
        {
            return false;
        }
        
        // Capture username for use in background thread
        string currentUserName = userName;
          // Variables to capture results from background thread
        bool processResult = false;
        string errorMessage = null;
        string debugMessage = null;
        bool timedOut = false;
        int exitCode = 0;
            
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "wsl";
                        // Suppress "bogus screen size" warning by redirecting stderr to /dev/null for this command
                        process.StartInfo.Arguments = $"-d Debian -u {currentUserName} -- ps aux 2>/dev/null | grep spacetimedb-standalone | grep -v grep";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;

                        process.Start();
                        string output = process.StandardOutput.ReadToEnd().Trim();
                        string error = process.StandardError.ReadToEnd().Trim();
                        process.WaitForExit(3000);
                        
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch {}
                            return new { Success = false, Error = error, Debug = "timed out", TimedOut = true, ExitCode = -1, Exception = (string)null };
                        }

                        bool running = process.ExitCode == 0 && output.Contains("spacetimedb-standalone");
                        
                        return new { Success = running, Error = error, Debug = running ? "running" : "not running", TimedOut = false, ExitCode = process.ExitCode, Exception = (string)null };
                    }
                }            
                catch (Exception ex)
                {
                    return new { Success = false, Error = (string)null, Debug = (string)null, TimedOut = false, ExitCode = -1, Exception = ex.Message };
                }
            });
            
            // Handle results on main thread
            if (!string.IsNullOrEmpty(result.Exception))
            {
                if (debugMode) logCallback($"WSL process check exception: {result.Exception}", -1);
                return false;
            }
            
            processResult = result.Success;
            errorMessage = result.Error;
            debugMessage = result.Debug;
            timedOut = result.TimedOut;
            exitCode = result.ExitCode;
            
            // Log results on main thread
            if (timedOut && debugMode)
            {
                logCallback("WSL process check timed out", -1);
                return false;
            }
              if (debugMode && !string.IsNullOrEmpty(errorMessage))
            {
                // Filter out the common "bogus screen size" warning that's not useful
                if (!errorMessage.Contains("screen size is bogus"))
                {
                    logCallback($"WSL process check stderr: {errorMessage}", 0);
                }
            }
            
            if (debugMode && !string.IsNullOrEmpty(debugMessage))
            {
                logCallback($"WSL process check result: {debugMessage} (exit code: {exitCode})", processResult ? 1 : 0);
            }
            
            return processResult;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error during server status check: {ex.Message}", -1);
            return false;
        }
    }
    #endregion
    
    #region CheckPrereq

    public void CheckPrerequisites(Action<bool, bool, bool, bool, bool, bool, bool, bool, bool, bool> callback)
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
            // WSL Check
            "Write-Host 'Checking WSL...'; " +
            "$wslExePath = Join-Path $env:SystemRoot 'System32\\wsl.exe'; " +
            "$lxssRegPath = 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Lxss'; " +
            "$wslInstalled = ((Test-Path $wslExePath) -and (Test-Path $lxssRegPath)); " +
            "if ($wslInstalled) { Write-Host 'WSL_INSTALLED=TRUE' } else { Write-Host 'WSL_INSTALLED=FALSE' }; " +
            
            // Conditional execution based on WSL
            "if ($wslInstalled) { " +
                // All WSL-dependent checks run independently if WSL is installed
                // Debian Check
                "Write-Host 'Checking Debian...'; " +
                "wsl --list 2>&1 | Select-String -Pattern 'Debian' -Quiet; " +
                "if ($?) { Write-Host 'DEBIAN_INSTALLED=TRUE' } else { Write-Host 'DEBIAN_INSTALLED=FALSE' }; " +
                
                // Debian Trixie Check
                "Write-Host 'Checking Debian Trixie...'; " +
                "$trixie = (wsl -d Debian -u "+ userName + " -- cat /etc/os-release 2>&1); " +
                "if ($trixie -match 'trixie') { Write-Host 'TRIXIE_INSTALLED=TRUE' } else { Write-Host 'TRIXIE_INSTALLED=FALSE' }; " +
                
                // cURL Check
                "Write-Host 'Checking curl...'; " +
                "$curl = (wsl -d Debian -u "+ userName + " -- which curl 2>&1); " +
                "if ($curl -match '/usr/bin/curl') { Write-Host 'CURL_INSTALLED=TRUE' } else { Write-Host 'CURL_INSTALLED=FALSE' }; " +
                
                // SpacetimeDB Check
                "Write-Host 'Checking SpacetimeDB...'; " +
                "$spacetime = (wsl -d Debian -u " + userName + " -- bash -l -c '\"ls -l $HOME/.local/bin\"' 2>&1); " +
                "if ($spacetime -match 'spacetime') { Write-Host 'SPACETIMEDB_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDB_INSTALLED=FALSE' }; " +
                
                // SpacetimeDB Path Check
                "Write-Host 'Checking SpacetimeDB PATH...'; " +
                "$spacetime = (wsl -d Debian -u " + userName + " -- bash -l -c '\"which spacetime\"' 2>&1); " +
                "if ($spacetime -match 'spacetime') { Write-Host 'SPACETIMEDBPATH_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDBPATH_INSTALLED=FALSE' }; " +
                
                // Rust Check
                "Write-Host 'Checking rustup...'; " +
                "$rust = (wsl -d Debian -u "+ userName + " -- bash -l -c '\"which rustup\"' 2>&1); " +
                "if ($rust -match 'rustup') { Write-Host 'RUST_INSTALLED=TRUE' } else { Write-Host 'RUST_INSTALLED=FALSE' }; " +
                
                // SpacetimeDB Service Check
                "Write-Host 'Checking SpacetimeDB Service...'; " +
                "$service = (wsl -d Debian -u " + userName + " -- systemctl is-enabled spacetimedb.service 2>&1); " +
                "if ($service -match 'enabled') { Write-Host 'SPACETIMEDBSERVICE_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDBSERVICE_INSTALLED=FALSE' }; " +
                
                // SpacetimeDB Logs Service Check
                "Write-Host 'Checking SpacetimeDB Logs Service...'; " +
                "$logsService = (wsl -d Debian -u " + userName + " -- systemctl status spacetimedb-logs.service 2>&1); " +
                "if ($logsService -match 'spacetimedb-logs.service') { Write-Host 'SPACETIMEDBLOGSSERVICE_INSTALLED=TRUE' } else { Write-Host 'SPACETIMEDBLOGSSERVICE_INSTALLED=FALSE' }; " +
                
                // Binaryen Check
                "Write-Host 'Checking Binaryen...'; " +
                "$binaryen = (wsl -d Debian -u " + userName + " -- bash -c 'test -f /usr/local/bin/wasm-opt && echo \"wasm-opt found\" || echo \"not found\"' 2>&1); " +
                "if ($binaryen -match 'wasm-opt found') { Write-Host 'BINARYEN_INSTALLED=TRUE' } else { Write-Host 'BINARYEN_INSTALLED=FALSE' }" +
            "} else { " +
                // Set all dependent checks to FALSE if WSL is not installed
                "Write-Host 'DEBIAN_INSTALLED=FALSE'; " +
                "Write-Host 'TRIXIE_INSTALLED=FALSE'; " +
                "Write-Host 'CURL_INSTALLED=FALSE'; " +
                "Write-Host 'SPACETIMEDB_INSTALLED=FALSE'; " +
                "Write-Host 'SPACETIMEDBPATH_INSTALLED=FALSE'; " +
                "Write-Host 'RUST_INSTALLED=FALSE'; " +
                "Write-Host 'SPACETIMEDBSERVICE_INSTALLED=FALSE'; " +
                "Write-Host 'SPACETIMEDBLOGSSERVICE_INSTALLED=FALSE'; " +
                "Write-Host 'BINARYEN_INSTALLED=FALSE'" +
            "}\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        
        process.Start();
        
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        
        if (debugMode) logCallback(output, 0);
        
        // Parse output
        bool hasWSL = output.Contains("WSL_INSTALLED=TRUE");
        bool hasDebian = output.Contains("DEBIAN_INSTALLED=TRUE");
        bool hasDebianTrixie = output.Contains("TRIXIE_INSTALLED=TRUE");
        bool hasCurl = output.Contains("CURL_INSTALLED=TRUE");
        bool hasSpacetimeDB = output.Contains("SPACETIMEDB_INSTALLED=TRUE");
        bool hasSpacetimeDBPath = output.Contains("SPACETIMEDBPATH_INSTALLED=TRUE");
        bool hasRust = output.Contains("RUST_INSTALLED=TRUE");
        bool hasSpacetimeDBService = output.Contains("SPACETIMEDBSERVICE_INSTALLED=TRUE");
        bool hasSpacetimeDBLogsService = output.Contains("SPACETIMEDBLOGSSERVICE_INSTALLED=TRUE");
        bool hasBinaryen = output.Contains("BINARYEN_INSTALLED=TRUE");
        //logCallback($"Pre-requisites check complete. WSL: {hasWSL}, Debian: {hasDebian}, Debian Trixie: {hasDebianTrixie}, curl: {hasCurl}, SpacetimeDB: {hasSpacetimeDB}, SpacetimeDB Path: {hasSpacetimeDBPath}, Rust: {hasRust}, Service: {hasSpacetimeDBService}, Logs Service: {hasSpacetimeDBLogsService}, Binaryen: {hasBinaryen}", 0);
        if (!hasWSL || !hasDebian || !hasDebianTrixie || !hasCurl || !hasSpacetimeDB || !hasSpacetimeDBPath)
        {
            logCallback("Missing pre-requisites. Install manually or with the Server Installer Window.", -2);
        } else
        {
            logCallback("Pre-requisites check complete. All required components are installed.", 1);
        }
        callback(hasWSL, hasDebian, hasDebianTrixie, hasCurl, hasSpacetimeDB, hasSpacetimeDBPath, hasRust, hasSpacetimeDBService, hasSpacetimeDBLogsService, hasBinaryen);
    }
    #endregion

    #region StopServer
    public async Task<bool> StopServer(string commandPattern = null)
    {
        // Validate username before proceeding
        if (!ValidateUserName(false, out _)) return false;

        try
        {   
            // Check if SpacetimeDB service is enabled first
            bool hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", false);
            
            if (hasSpacetimeDBService)
            {
                // Use service-based stopping
                return await StopSpacetimeDBServices();
            }
            else
            {
                // Fallback to process-based stopping for non-service installations
                logCallback("SpacetimeDB service not configured. Attempting to stop processes manually...", 0);
                return await StopSpacetimeDBProcesses();
            }
        }
        catch (Exception ex)
        {
            logCallback($"Error stopping server: {ex.Message}", -1);
            return false;
        }
    }
    
    private async Task<bool> StopSpacetimeDBProcesses()
    {
        try
        {
            // Process-based stopping for non-service installations
            logCallback("Attempting to stop SpacetimeDB processes...", 0);
            
            // Try multiple stop strategies to ensure we catch all possible running processes
            
            // Strategy 1: Stop using PID file if it exists
            bool pidFileExists = RunWslCommandSilent($"test -f {WslPidPath}") == 0;
            if (pidFileExists)
            {
                if (debugMode) logCallback("Found PID file, attempting graceful stop via PID...", 0);
                int termResult = RunWslCommandSilent($"kill -TERM $(cat {WslPidPath}) 2>/dev/null");
                await Task.Delay(2000); // Give it time to shut down gracefully
                
                // Check if it's still running
                bool stillRunningViaPid = RunWslCommandSilent($"kill -0 $(cat {WslPidPath}) 2>/dev/null") == 0;
                if (!stillRunningViaPid)
                {
                    logCallback("Server stopped successfully via PID file.", 1);
                    // Clean up PID file
                    RunWslCommandSilent($"rm -f {WslPidPath}");
                }
                else
                {
                    // Force kill via PID
                    if (debugMode) logCallback("Graceful stop failed, forcing stop via PID...", 0);
                    RunWslCommandSilent($"kill -KILL $(cat {WslPidPath}) 2>/dev/null");
                    await Task.Delay(1000);
                    RunWslCommandSilent($"rm -f {WslPidPath}");
                }
            }
            
            // Strategy 2: Stop spacetimedb-standalone processes (what status check looks for)
            if (debugMode) logCallback("Stopping spacetimedb-standalone processes...", 0);
            int termExitCode1 = RunWslCommandSilent("pkill --signal TERM spacetimedb-standalone 2>/dev/null"); 
            await Task.Delay(1000);
            
            // Strategy 3: Stop any spacetime processes
            if (debugMode) logCallback("Stopping any remaining spacetime processes...", 0);
            int termExitCode2 = RunWslCommandSilent($"pkill --signal TERM -f \"spacetime start\" 2>/dev/null");
            await Task.Delay(1000);
            
            // Force kill any remaining processes
            int killExitCode1 = RunWslCommandSilent("pkill --signal KILL spacetimedb-standalone 2>/dev/null");
            int killExitCode2 = RunWslCommandSilent($"pkill --signal KILL -f \"spacetime start\" 2>/dev/null");
            await Task.Delay(500);
            
            if (debugMode) logCallback($"Process termination results - spacetimedb-standalone TERM: {termExitCode1}, spacetime TERM: {termExitCode2}, spacetimedb-standalone KILL: {killExitCode1}, spacetime KILL: {killExitCode2}", 0);

            // Kill the Windows process if it exists
            try 
            {
                if (serverProcess != null && !serverProcess.HasExited)
                {
                    serverProcess.Kill();
                    logCallback("Killed Windows server process.", 0);
                }
            }
            catch (Exception ex) 
            {
                logCallback($"Error killing Windows server process: {ex.Message}", -1);
            }
            
            // Clear process reference
            serverProcess = null;
            
            // Final verification - check if any spacetime processes are still running
            await Task.Delay(1000); // Give processes time to fully terminate
            
            bool anySpacetimeRunning = await Task.Run(() =>
            {
                try
                {
                    using (Process checkProcess = new Process())
                    {
                        checkProcess.StartInfo.FileName = "wsl";
                        checkProcess.StartInfo.Arguments = $"-d Debian -u {userName} -- pgrep -f spacetime";
                        checkProcess.StartInfo.RedirectStandardOutput = true;
                        checkProcess.StartInfo.UseShellExecute = false;
                        checkProcess.StartInfo.CreateNoWindow = true;
                        
                        checkProcess.Start();
                        string output = checkProcess.StandardOutput.ReadToEnd().Trim();
                        checkProcess.WaitForExit(3000);
                        
                        if (!checkProcess.HasExited)
                        {
                            try { checkProcess.Kill(); } catch {}
                            return false;
                        }
                        
                        return checkProcess.ExitCode == 0 && !string.IsNullOrEmpty(output);
                    }
                }
                catch
                {
                    return false;
                }
            });
            
            if (!anySpacetimeRunning)
            {
                logCallback("Server stop verified - no spacetime processes detected.", 1);
                return true;
            }
            else
            {
                logCallback("WARNING: Some spacetime processes may still be running after stop attempts.", -1);
                return false;
            }
        }
        catch (Exception ex)
        {
            logCallback($"Error stopping SpacetimeDB processes: {ex.Message}", -1);
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
    #endregion

    #region RunWSLCommand

    public int RunWslCommandSilent(string bashCommand)
    {
        // Validate username before proceeding - silent mode suppresses errors on first run
        if (!ValidateUserName(true, out _)) return -1;

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
            
            // Configure UTF-8 encoding to properly handle special characters from WSL
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            
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
    #endregion
    
    #region RunServerCommand

    public async Task<(string output, string error, bool success)> RunServerCommandAsync(string command, string serverDirectory = null, bool isStatusCheck = false)
    {
        // Validate username before proceeding - silent mode for status checks, non-silent for user actions
        if (!ValidateUserName(isStatusCheck, out bool shouldSuppressErrors))
        {
            // Return appropriate error message based on suppression setting
            string errorMessage = shouldSuppressErrors ? string.Empty : "Error: No Debian username set. Please set a valid username in the Server Window.";
            return (string.Empty, errorMessage, false);
        }

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
            
            // Configure UTF-8 encoding to properly handle special characters from WSL
            wslProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            wslProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            
            wslProcess.Start();
            
            // Read streams asynchronously with Task
            var outputTask = wslProcess.StandardOutput.ReadToEndAsync();
            var errorTask = wslProcess.StandardError.ReadToEndAsync();
            
            // Log debug info
            string debug = $"-d Debian -u {userName} --exec bash -l -c \"{fullCommand}\"";
            if (debugMode) logCallback("Debug: " + debug, 0);
            
            // Wait for completion or timeout
            bool exited = await Task.Run(() => wslProcess.WaitForExit(90000)); // 90 seconds timeout
            
            if (!exited)
            {
                logCallback("WSL command timed out after 90 seconds. Attempting to kill process...", -1);
                try { wslProcess.Kill(); } catch (Exception killEx) { logCallback($"Error killing timed-out process: {killEx.Message}", -1); }
            }
            
            // Get the output and error
            string output = await outputTask;
            string error = await errorTask;
            
            // Analyze the result
            bool commandSuccess = false;
            bool isPublishCommand = command.Contains("spacetime publish");
            bool isGenerateCommand = command.Contains("spacetime generate");
            bool isLogSizeCommand = command.Contains("du -s") || command.Contains("journalctl") && command.Contains("wc -c");
            
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
            else if (isLogSizeCommand && !string.IsNullOrEmpty(output) && output.Trim().All(char.IsDigit))
            {
                // Log size commands are successful if they return numeric output
                commandSuccess = true;
                if (debugMode) logCallback($"Log size command successful with output: {output.Trim()}", 1);
            }
            else if (wslProcess.ExitCode == 0)
            {
                // Command succeeded based on exit code
                commandSuccess = true;
            }
            else if (string.IsNullOrEmpty(output) && string.IsNullOrEmpty(error))
            {
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
    
    private (string executable, string arguments) SplitCommandForProcess(string command)
    {
        command = command.Trim();
        string executable;
        string arguments = string.Empty;

        if (command.StartsWith("\"")) // Quoted executable path
        {
            int closingQuoteIndex = command.IndexOf('\"', 1);
            if (closingQuoteIndex > 0)
            {
                executable = command.Substring(0, closingQuoteIndex + 1);
                if (command.Length > closingQuoteIndex + 1)
                {
                    arguments = command.Substring(closingQuoteIndex + 1).TrimStart();
                }
            }
            else // Malformed, treat as single unquoted
            {
                executable = command;
            }
        }
        else // Unquoted executable path
        {
            int firstSpaceIndex = command.IndexOf(' ');
            if (firstSpaceIndex > 0)
            {
                executable = command.Substring(0, firstSpaceIndex);
                arguments = command.Substring(firstSpaceIndex + 1).TrimStart();
            }
            else
            {
                executable = command;
            }
        }
        // Ensure executable is unquoted if it was parsed that way, for Start-Process -FilePath
        return (executable.Trim('\"'), arguments);
    }
    
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

    #region PingServer
    
    public void PingServer(string serverUrl, Action<bool, string> callback)
    {
        // Validate username before proceeding
        if (!ValidateUserName(false, out bool shouldSuppressErrors))
        {
            // Return appropriate error message based on suppression setting
            string errorMessage = shouldSuppressErrors ? string.Empty : "Error: No Debian username set. Please set a valid username in the Server Window.";
            callback(false, errorMessage);
            return;
        }

        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "wsl.exe";
            process.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"spacetime server ping {serverUrl}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            
            // Configure UTF-8 encoding to properly handle special characters from WSL
            process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            process.StartInfo.StandardErrorEncoding = Encoding.UTF8;
            
            process.Start();
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            // Combine output and error for parsing
            string fullOutput = output + error;
            
            //if (debugMode) UnityEngine.Debug.Log($"[ServerCMDProcess] Ping result: {fullOutput}");
            
            // Check if server is online by looking for "Server is online" in the output
            bool isOnline = fullOutput.Contains("Server is online");
            
            // Prepare a clean message for display
            string message = fullOutput;
            
            // Remove the "WARNING: This command is UNSTABLE" line if present
            if (message.Contains("WARNING: This command is UNSTABLE"))
            {
                int warningIndex = message.IndexOf("WARNING:");
                int newlineIndex = message.IndexOf('\n', warningIndex);
                if (newlineIndex > warningIndex)
                {
                    message = message.Remove(warningIndex, newlineIndex + 1 - warningIndex);
                }
            }
            
            // Trim and clean up the message
            message = message.Trim();
            
            callback(isOnline, message);
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerCMDProcess] Error pinging server: {ex.Message}");
            callback(false, $"Error pinging server: {ex.Message}");
        }
    }
    
    #endregion
    
    #region PowerShellCommand
    
    public void RunPowerShellCommand(string command, Action<string, int> logCallback)
    {
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            logCallback($"Running PowerShell command: {command}", 0);
            process.Start();
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            // Log the output
            if (!string.IsNullOrEmpty(output))
            {
                logCallback(output, 1);
            }
            
            // Log any errors
            if (!string.IsNullOrEmpty(error))
            {
                logCallback(error, -1);
            }
            
            // Log completion status
            if (process.ExitCode == 0)
            {
                logCallback($"Command completed successfully with exit code: {process.ExitCode}", 1);
            }
            else
            {
                logCallback($"Command failed with exit code: {process.ExitCode}", -1);
            }
        }
        catch (Exception ex)
        {
            logCallback($"Error executing PowerShell command: {ex.Message}", -1);
        }
    }
    
    #endregion

    #region Service Management
    
    public async Task<bool> StartSpacetimeDBServices()
    {
        if (!ValidateUserName(false, out _)) return false;

        try
        {
            // Check if SpacetimeDB service is available
            bool hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", false);
            bool hasSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", false);
            
            if (!hasSpacetimeDBService)
            {
                logCallback("SpacetimeDB service is not configured. Cannot start services.", -1);
                return false;
            }

            // Start the main SpacetimeDB service
            var result = await RunServerCommandAsync("sudo systemctl start spacetimedb.service");
            
            if (!result.success)
            {
                logCallback($"Failed to start SpacetimeDB service: {result.error}", -1);
                return false;
            }

            if (debugMode) logCallback("SpacetimeDB service started successfully.", 1);

            // Start the logs service if configured
            if (hasSpacetimeDBLogsService)
            {
                if (debugMode) logCallback("Starting SpacetimeDB logs service...", 0);
                var resultLogService = await RunServerCommandAsync("sudo systemctl start spacetimedb-logs.service");
                
                if (!resultLogService.success)
                {
                    logCallback($"Failed to start SpacetimeDB logs service: {resultLogService.error}", -1);
                    // Don't return false here as the main service started successfully
                }
                else
                {
                    if (debugMode) logCallback("SpacetimeDB logs service started successfully.", 1);
                }
            }
            else
            {
                if (debugMode) logCallback("SpacetimeDB logs service is not configured. Skipping.", 0);
            }

            return true;
        }
        catch (Exception ex)
        {
            logCallback($"Error starting SpacetimeDB services: {ex.Message}", -1);
            return false;
        }
    }    

    public async Task<bool> StopSpacetimeDBServices()
    {
        if (!ValidateUserName(false, out _)) return false;

        try
        {
            // Check if SpacetimeDB service is available
            bool hasSpacetimeDBService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBService", false);
            bool hasSpacetimeDBLogsService = EditorPrefs.GetBool(PrefsKeyPrefix + "HasSpacetimeDBLogsService", false);

            bool stopSuccess = false;

            // Strategy 1: Try to stop services if they're configured and try a quick stop first
            if (hasSpacetimeDBService)
            {
                if (debugMode) logCallback("Attempting to stop services using systemctl...", 0);
                
                // Try stopping services with a shorter timeout to avoid hanging
                var tasks = new List<Task<(string output, string error, bool success)>>();
                
                if (hasSpacetimeDBLogsService)
                {
                    tasks.Add(RunServerCommandWithTimeoutAsync("sudo systemctl stop spacetimedb-logs.service", 10000)); // 10 second timeout
                }
                tasks.Add(RunServerCommandWithTimeoutAsync("sudo systemctl stop spacetimedb.service", 10000)); // 10 second timeout

                // Wait for all tasks to complete or timeout
                var results = await Task.WhenAll(tasks);
                
                // Check if any succeeded
                stopSuccess = results.Any(r => r.success);
                
                if (stopSuccess)
                {
                    if (debugMode) logCallback("Service stop commands completed successfully.", 1);
                    return true;
                }
                else
                {
                    logCallback("Service stop commands failed or timed out.", -1);
                    return false;
                }
            }
            else
            {
                // Strategy 2: Direct process termination (always try this for reliability)
                if (debugMode) logCallback("Stopping SpacetimeDB processes directly...", 0);
                
                // Kill spacetimedb-standalone processes (the main server)
                var killResult1 = await RunServerCommandWithTimeoutAsync("pkill -TERM spacetimedb-standalone", 5000);
                await Task.Delay(2000); // Give graceful termination time
                
                // Force kill if still running
                var killResult2 = await RunServerCommandWithTimeoutAsync("pkill -KILL spacetimedb-standalone", 5000);
                
                // Kill any remaining spacetime processes
                var killResult3 = await RunServerCommandWithTimeoutAsync("pkill -TERM -f 'spacetime'", 5000);
                await Task.Delay(1000);
                var killResult4 = await RunServerCommandWithTimeoutAsync("pkill -KILL -f 'spacetime'", 5000);

                // Strategy 3: Clean up any hanging sudo processes
                if (debugMode) logCallback("Cleaning up any hanging sudo processes...", 0);
                await RunServerCommandWithTimeoutAsync("pkill -KILL -f 'sudo systemctl'", 5000);

                // Verify that processes are actually stopped
                await Task.Delay(2000);
                var checkResult = await RunServerCommandWithTimeoutAsync("pgrep -f spacetime", 5000);
                
                if (string.IsNullOrEmpty(checkResult.output))
                {
                    logCallback("SpacetimeDB processes stopped successfully.", 1);
                    return true;
                }
                else
                {
                    logCallback("Some SpacetimeDB processes may still be running. Manual cleanup may be required.", -1);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            logCallback($"Error stopping SpacetimeDB services: {ex.Message}", -1);
            return false;
        }
    }

    // Helper method for running commands with a specific timeout
    private async Task<(string output, string error, bool success)> RunServerCommandWithTimeoutAsync(string command, int timeoutMs)
    {
        if (!ValidateUserName(true, out bool shouldSuppressErrors)) // Silent mode for internal operations
        {
            // Return appropriate error message based on suppression setting
            string errorMessage = shouldSuppressErrors ? string.Empty : "Error: No Debian username set.";
            return (string.Empty, errorMessage, false);
        }

        try
        {
            using (var wslProcess = new Process())
            {
                wslProcess.StartInfo.FileName = "wsl.exe";
                wslProcess.StartInfo.Arguments = $"-d Debian -u {userName} --exec bash -l -c \"{command.Replace("\"", "\\\"")}\"";
                wslProcess.StartInfo.UseShellExecute = false;
                wslProcess.StartInfo.CreateNoWindow = true;
                wslProcess.StartInfo.RedirectStandardOutput = true;
                wslProcess.StartInfo.RedirectStandardError = true;
                wslProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
                wslProcess.StartInfo.StandardErrorEncoding = Encoding.UTF8;

                wslProcess.Start();

                var outputTask = wslProcess.StandardOutput.ReadToEndAsync();
                var errorTask = wslProcess.StandardError.ReadToEndAsync();

                bool completed = await Task.Run(() => wslProcess.WaitForExit(timeoutMs));

                if (!completed)
                {
                    if (debugMode) logCallback($"Command timed out after {timeoutMs}ms: {command}", -1);
                    try { wslProcess.Kill(); } catch { }
                    return (string.Empty, "Command timed out", false);
                }

                string output = await outputTask;
                string error = await errorTask;

                // Consider the command successful if it exits with code 0 OR if it's a kill command (which may not find processes)
                bool success = wslProcess.ExitCode == 0 || command.Contains("pkill");

                return (output, error, success);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error running command with timeout: {ex.Message}", -1);
            return (string.Empty, ex.Message, false);
        }
    }

    // Method to check if SpacetimeDB service is running with caching
    public async Task<bool> CheckServerRunning(bool instantCheck = false)
    {
        if (!ValidateUserName(true, out _)) return false;

        double currentTime = EditorApplication.timeSinceStartup;
        if (!instantCheck)
        {
            if (currentTime - lastStatusCacheTime < statusCacheTimeout)
            {
                return cachedServerRunningStatus;
            }
        }

        // Check SpacetimeDB service status using systemctl
        bool running = false;

        try
        {
            var result = await RunServerCommandAsync("systemctl is-active spacetimedb.service", null, true);
            running = result.output.Trim() == "active";

            if (debugMode)
            {
                logCallback($"Service check result: {(running ? "active" : result.output.Trim())}", running ? 1 : 0);
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error checking service: {ex.Message}", -1);
            running = false;
        }

        // Update cache
        cachedServerRunningStatus = running;
        lastStatusCacheTime = currentTime;

        return running;
    }

    // Method to clear the cached status (useful when server state changes)
    public void ClearStatusCache()
    {
        lock (statusUpdateLock)
        {
            cachedServerRunningStatus = false;
            lastStatusCacheTime = 0;
            if (debugMode) logCallback("Server status cache cleared", 0);
        }
    }

    #endregion

    #region WSL Log Management

    // Get the size of /var/log/journal/ directory in MB for WSL
    public async Task<float> GetWSLJournalSize()
    {
        if (!ValidateUserName(true, out _))
        {
            if (debugMode) logCallback("Username not configured. Cannot get WSL log size.", -1);
            return -1f;
        }

        if (debugMode) logCallback($"WSL GetWSLJournalSize: Using username '{userName}'", 0);

        try
        {
            // Use du command to get directory size in KB, then convert to MB
            // -s = summarize (don't show subdirectories)
            // -k = show size in KB
            var result = await RunServerCommandAsync("du -sk /var/log/journal/ 2>/dev/null | head -1", null, true);
            
            if (debugMode) logCallback($"WSL GetWSLJournalSize: Command result - Success: {result.success}, Output: '{result.output}', Error: '{result.error}'", 0);
            
            if (!result.success)
            {
                if (debugMode) logCallback($"Failed to get WSL log directory size. Error: {result.error}", -1);
                return -1f;
            }
            
            // Parse the output - du returns format like "12345  /var/log/journal/"
            string output = result.output.Trim();
            if (string.IsNullOrEmpty(output))
            {
                if (debugMode) logCallback("Empty output when checking WSL log size", -1);
                return -1f;
            }
            
            if (debugMode) logCallback($"WSL raw du output: '{output}'", 0);
            
            // Extract the size (first part before whitespace)
            string[] parts = output.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                if (debugMode) logCallback("Unable to parse WSL log size output - no parts found", -1);
                return -1f;
            }
            
            string sizeString = parts[0];
            if (debugMode) logCallback($"Attempting to parse WSL size string: '{sizeString}'", 0);
            
            if (long.TryParse(sizeString, out long sizeInKB))
            {
                // Convert KB to MB (1 MB = 1024 KB)
                float sizeInMB = sizeInKB / 1024.0f;
                
                if (debugMode) logCallback($"WSL log directory size: {sizeInKB} KB ({sizeInMB:F2} MB)", 0);
                
                return sizeInMB;
            }
            else
            {
                if (debugMode) logCallback($"Failed to parse WSL size value: '{sizeString}' from output: '{output}'", -1);
                return -1f;
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error getting WSL log size: {ex.Message}", -1);
            return -1f;
        }
    }

    // Get the size of spacetimedb and spacetimedb-logs service logs in MB for WSL
    public async Task<(float spacetimedbSizeMB, float spacetimedbLogsSizeMB)> GetWSLSpacetimeLogSizes()
    {
        if (!ValidateUserName(true, out _))
        {
            if (debugMode) logCallback("Username not configured. Cannot get WSL service log sizes.", -1);
            return (-1f, -1f);
        }

        if (debugMode) logCallback($"WSL GetWSLSpacetimeLogSizes: Using username '{userName}'", 0);

        try
        {
            // Get spacetimedb.service log size
            var spacetimedbResult = await RunServerCommandAsync("journalctl -u spacetimedb.service --output=short-iso -q | wc -c", null, true);
            float spacetimedbSizeMB = -1f;
            
            if (debugMode) logCallback($"WSL GetWSLSpacetimeLogSizes (spacetimedb): Command result - Success: {spacetimedbResult.success}, Output: '{spacetimedbResult.output}', Error: '{spacetimedbResult.error}'", 0);
            
            if (spacetimedbResult.success)
            {
                string spacetimedbOutput = spacetimedbResult.output.Trim();
                if (debugMode) logCallback($"WSL spacetimedb.service log size output: '{spacetimedbOutput}'", 0);
                
                if (long.TryParse(spacetimedbOutput, out long spacetimedbSizeBytes))
                {
                    // Convert bytes to MB (1 MB = 1024 * 1024 bytes)
                    spacetimedbSizeMB = spacetimedbSizeBytes / (1024.0f * 1024.0f);
                    if (debugMode) logCallback($"WSL spacetimedb.service log size: {spacetimedbSizeBytes} bytes ({spacetimedbSizeMB:F2} MB)", 0);
                }
                else
                {
                    if (debugMode) logCallback($"Failed to parse WSL spacetimedb.service log size: '{spacetimedbOutput}'", -1);
                }
            }
            else
            {
                if (debugMode) logCallback($"Failed to get WSL spacetimedb.service log size. Error: {spacetimedbResult.error}", -1);
            }
            
            // Get spacetimedb-logs.service log size
            var spacetimedbLogsResult = await RunServerCommandAsync("journalctl -u spacetimedb-logs.service --output=short-iso -q | wc -c", null, true);
            float spacetimedbLogsSizeMB = -1f;
            
            if (debugMode) logCallback($"WSL GetWSLSpacetimeLogSizes (spacetimedb-logs): Command result - Success: {spacetimedbLogsResult.success}, Output: '{spacetimedbLogsResult.output}', Error: '{spacetimedbLogsResult.error}'", 0);
            
            if (spacetimedbLogsResult.success)
            {
                string spacetimedbLogsOutput = spacetimedbLogsResult.output.Trim();
                if (debugMode) logCallback($"WSL spacetimedb-logs.service log size output: '{spacetimedbLogsOutput}'", 0);
                
                if (long.TryParse(spacetimedbLogsOutput, out long spacetimedbLogsSizeBytes))
                {
                    // Convert bytes to MB (1 MB = 1024 * 1024 bytes)
                    spacetimedbLogsSizeMB = spacetimedbLogsSizeBytes / (1024.0f * 1024.0f);
                    if (debugMode) logCallback($"WSL spacetimedb-logs.service log size: {spacetimedbLogsSizeBytes} bytes ({spacetimedbLogsSizeMB:F2} MB)", 0);
                }
                else
                {
                    if (debugMode) logCallback($"Failed to parse WSL spacetimedb-logs.service log size: '{spacetimedbLogsOutput}'", -1);
                }
            }
            else
            {
                if (debugMode) logCallback($"Failed to get WSL spacetimedb-logs.service log size. Error: {spacetimedbLogsResult.error}", -1);
            }
            
            if (debugMode) logCallback($"WSL service log sizes - spacetimedb: {spacetimedbSizeMB:F2} MB, spacetimedb-logs: {spacetimedbLogsSizeMB:F2} MB", 0);
            
            return (spacetimedbSizeMB, spacetimedbLogsSizeMB);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error getting WSL service log sizes: {ex.Message}", -1);
            return (-1f, -1f);
        }
    }

    #endregion

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games