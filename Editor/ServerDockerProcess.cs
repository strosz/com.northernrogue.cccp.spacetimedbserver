using UnityEngine;
using System.Diagnostics;
using System;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using NorthernRogue.CCCP.Editor.Settings;

// Runs the local Docker server processes and methods ///

namespace NorthernRogue.CCCP.Editor {

public class ServerDockerProcess
{
    public static bool debugMode = false;
    
    // Docker container name for SpacetimeDB
    public const string ContainerName = "spacetimedb-server";
    // Official SpacetimeDB Docker image name (as per documentation)
    public const string ImageName = "clockworklabs/spacetime";
    
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
    
    public ServerDockerProcess(Action<string, int> logCallback, bool debugMode = false)
    {
        this.logCallback = logCallback;
        ServerDockerProcess.debugMode = debugMode;
        
        if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Initialized");
    }
    
    #region Installation

    public async Task<bool> RunPowerShellInstallCommand(string command, Action<string, int> statusCallback = null, bool visibleProcess = true, bool keepWindowOpenForDebug = false, bool requiresElevation = false)
    {
        Process process = null;
        try
        {
            if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Running PowerShell command: {command} | Visible: {visibleProcess} | KeepOpen: {keepWindowOpenForDebug} | RequiresElevation: {requiresElevation}");
            
            process = new Process();
            
            if (visibleProcess)
            {
                // For visible window, use a simpler approach
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.CreateNoWindow = false;
                
                // Use a temporary batch file
                string tempBatchFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cosmoscovecontrolpanel_docker_{DateTime.Now.Ticks}.bat");
                
                using (System.IO.StreamWriter sw = new System.IO.StreamWriter(tempBatchFile))
                {
                    sw.WriteLine("@echo off");
                    sw.WriteLine("echo Running command...");
                    sw.WriteLine(command);
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
                        sw.WriteLine("echo Window will close in 5 seconds...");
                        sw.WriteLine("timeout /t 5 /nobreak > nul");
                    }
                    sw.WriteLine("exit /b %ERRORLEVEL%");
                }
                
                process.StartInfo.Arguments = $"/C \"{tempBatchFile}\"";
                
                if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Created batch file: {tempBatchFile} with command: {command}");
            }
            else // Hidden execution
            {
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                string commandToExecute = command;
                if (requiresElevation)
                {
                    var (exe, args) = SplitCommandForProcess(commandToExecute);
                    
                    string escapedExe = exe.Replace("'", "''");
                    string escapedArgs = args.Replace("'", "''");

                    commandToExecute = $"$ProgressPreference = 'SilentlyContinue'; try {{ $process = Start-Process -FilePath '{escapedExe}' -ArgumentList '{escapedArgs}' -Verb RunAs -Wait -PassThru; exit $process.ExitCode; }} catch {{ Write-Error $_; exit 1; }}";
                    if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Elevated command for hidden execution: {commandToExecute}");
                }
                
                string escapedFinalCommand = commandToExecute.Replace("\"", "`\"");
                process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{escapedFinalCommand}\"";
            }
            
            process.Start();
            statusCallback?.Invoke($"Executing: {command.Split(' ')[0]}...", 0);
            
            string outputLog = "";
            string errorLog = "";

            if (!visibleProcess)
            {
                outputLog = await process.StandardOutput.ReadToEndAsync();
                errorLog = await process.StandardError.ReadToEndAsync();
            }

            await Task.Run(() => process.WaitForExit()); 
            int exitCode = process.ExitCode;

            if (debugMode && !visibleProcess) 
            {
                if (!string.IsNullOrEmpty(outputLog)) UnityEngine.Debug.Log($"[ServerDockerProcess] Output: {outputLog}");
                if (!string.IsNullOrEmpty(errorLog)) UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {errorLog}");
            }
            
            if (exitCode == 0)
            {
                statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' completed successfully.", 1);
                return true;
            }
            else
            {
                if (visibleProcess)
                {
                    if (debugMode) statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' window was closed. Check installation status to verify success.", 0);
                    if (debugMode) UnityEngine.Debug.Log($"[ServerDockerProcess] Visible process exited with code {exitCode} - likely manual window closure");
                    return true;
                }
                else
                {
                    if (debugMode) statusCallback?.Invoke($"Command '{command.Split(' ')[0]}...' failed with exit code {exitCode}. Check console/window for details.", -1);
                    if (!string.IsNullOrEmpty(errorLog)) 
                    {
                         UnityEngine.Debug.LogError($"[ServerDockerProcess] Hidden command failed. Error stream: {errorLog}");
                    } 
                    else if (!string.IsNullOrEmpty(outputLog)) 
                    {
                         UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Hidden command failed. Output stream: {outputLog}");
                    }
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            string commandExcerpt = command.Length > 50 ? command.Substring(0, 50) + "..." : command;
            statusCallback?.Invoke($"Error executing command '{commandExcerpt}': {ex.Message}", -1);
            UnityEngine.Debug.LogError($"[ServerDockerProcess] Exception: {ex}");
            return false;
        }
        finally
        {
            if (process != null && !visibleProcess)
            {
                process?.Dispose();
            }
        }
    }
    
    #endregion
    
    #region Process Execution Methods

    public Process StartVisibleServerProcess(string serverDirectory, string unityAssetsDirectory = null)
    {
        try 
        {
            // Get the configured port from settings
            int hostPort = CCCPSettings.Instance.serverPortDocker;
            
            // Check if container already exists and is running
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            if (exists && isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' is already running. Reusing existing container.", 0);
                
                // Just open a window to the existing container
                Process logProcess = new Process();
                logProcess.StartInfo.FileName = "cmd.exe";
                logProcess.StartInfo.Arguments = $"/k docker logs -f {ContainerName}";
                logProcess.StartInfo.UseShellExecute = true;
                logProcess.Start();
                return logProcess;
            }
            
            if (exists && !isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' exists but is stopped. Starting it...", 0);
                
                // Start existing stopped container
                Process startProcess = new Process();
                startProcess.StartInfo.FileName = "cmd.exe";
                startProcess.StartInfo.Arguments = $"/c docker start {ContainerName}";
                startProcess.StartInfo.UseShellExecute = false;
                startProcess.StartInfo.CreateNoWindow = true;
                startProcess.Start();
                startProcess.WaitForExit();
                
                // Now show logs
                Process logProcess = new Process();
                logProcess.StartInfo.FileName = "cmd.exe";
                logProcess.StartInfo.Arguments = $"/k docker logs -f {ContainerName}";
                logProcess.StartInfo.UseShellExecute = true;
                logProcess.Start();
                return logProcess;
            }
            
            // Container doesn't exist, create new one
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            
            // Build volume mounts
            string volumeMounts = $"-v \"{serverDirectory}:/app\"";
            
            // Add persistent volume for SpacetimeDB data
            volumeMounts += " -v spacetimedb-data:/home/spacetime/.local/share/spacetime/data";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB data", 0);
            
            // Add Unity Assets directory as a volume mount if provided
            if (!string.IsNullOrEmpty(unityAssetsDirectory))
            {
                // Get the Assets root directory (Unity project root contains Assets)
                string assetsRoot = GetUnityProjectRoot(unityAssetsDirectory);
                if (!string.IsNullOrEmpty(assetsRoot))
                {
                    volumeMounts += $" -v \"{assetsRoot}:/unity\"";
                    if (debugMode) logCallback($"Mounting Unity project at /unity: {assetsRoot}", 0);
                }
                else
                {
                    logCallback($"WARNING: Could not determine Unity project root from: {unityAssetsDirectory}. File generation may fail!", -1);
                    logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                    string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                    if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
                }
            }
            else
            {
                logCallback($"WARNING: Unity Assets directory not provided. File generation will likely fail!", -1);
                logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
            }
            
            // Start Docker container with interactive mode
            // Note: NOT using --rm so container persists and can be stopped/restarted
            string dockerCommand = $"docker run -it --name {ContainerName} -p {hostPort}:3000 {volumeMounts} {ImageName} start";
            
            process.StartInfo.Arguments = $"/k {dockerCommand}";
            process.StartInfo.UseShellExecute = true;
            
            if (debugMode) logCallback($"Starting SpacetimeDB Server on port {hostPort} (Docker Visible CMD)...", 0);
            process.Start();
            serverProcess = process;
            return process;
        }
        catch (Exception ex) 
        {
            logCallback($"Error starting visible Docker server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public Process StartSilentServerProcess(string serverDirectory, string unityAssetsDirectory = null)
    {
        try 
        {
            // Get the configured port from settings
            int hostPort = CCCPSettings.Instance.serverPortDocker;
            
            // Check if container already exists and is running
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            if (exists && isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' is already running. No need to start.", 1);
                return null; // Return null but this is not an error - container is already running
            }
            
            if (exists && !isRunning)
            {
                if (debugMode) logCallback($"Container '{ContainerName}' exists but is stopped. Starting it...", 0);
                
                // Start existing stopped container
                Process startProcess = new Process();
                startProcess.StartInfo.FileName = "cmd.exe";
                startProcess.StartInfo.Arguments = $"/c docker start {ContainerName}";
                startProcess.StartInfo.UseShellExecute = false;
                startProcess.StartInfo.CreateNoWindow = true;
                startProcess.StartInfo.RedirectStandardOutput = true;
                startProcess.StartInfo.RedirectStandardError = true;
                startProcess.Start();
                
                string startOutput = startProcess.StandardOutput.ReadToEnd();
                string startError = startProcess.StandardError.ReadToEnd();
                startProcess.WaitForExit();
                
                if (startProcess.ExitCode == 0)
                {
                    if (debugMode) logCallback($"Existing container started successfully.", 1);
                    return startProcess;
                }
                else
                {
                    logCallback($"Failed to start existing container. Error: {startError}", -1);
                    return null;
                }
            }
            
            // Build volume mounts
            string volumeMounts = $"-v \"{serverDirectory}:/app\"";
            
            // Add persistent volume for SpacetimeDB data
            volumeMounts += " -v spacetimedb-data:/home/spacetime/.local/share/spacetime/data";
            if (debugMode) logCallback("Mounting persistent volume for SpacetimeDB data", 0);
            
            // Add Unity Assets directory as a volume mount if provided
            if (!string.IsNullOrEmpty(unityAssetsDirectory))
            {
                // Get the Assets root directory (Unity project root contains Assets)
                string assetsRoot = GetUnityProjectRoot(unityAssetsDirectory);
                if (!string.IsNullOrEmpty(assetsRoot))
                {
                    volumeMounts += $" -v \"{assetsRoot}:/unity\"";
                    if (debugMode) logCallback($"Mounting Unity project at /unity: {assetsRoot}", 0);
                }
                else
                {
                    logCallback($"WARNING: Could not determine Unity project root from: {unityAssetsDirectory}. File generation may fail!", -1);
                    logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                    string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                    if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
                }
            }
            else
            {
                logCallback($"WARNING: Unity Assets directory not provided. File generation will likely fail!", -1);
                logCallback($"Using fallback: Mounting Unity's Application.dataPath", 0);
                string fallbackRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                volumeMounts += $" -v \"{fallbackRoot}:/unity\"";
                if (debugMode) logCallback($"Fallback mount: {fallbackRoot} -> /unity", 0);
            }
            
            // Container doesn't exist, create new one
            // Start Docker container in detached mode
            string dockerCommand = $"docker run -d --name {ContainerName} -p {hostPort}:3000 {volumeMounts} {ImageName} start";
            
            if (debugMode) logCallback($"Docker command: {dockerCommand}", 0);
            
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe"; 
            process.StartInfo.Arguments = $"/c {dockerCommand}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            if (debugMode) logCallback($"Creating new Docker container on port {hostPort}...", 0);
            process.Start();
            
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            
            if (process.ExitCode == 0)
            {
                if (debugMode) logCallback($"Docker container started successfully. Container ID: {output.Trim()}", 1);
            }
            else
            {
                logCallback($"Failed to start Docker container. Error: {error}", -1);
                return null;
            }
            
            serverProcess = process;
            return process;
        }
        catch (Exception ex) 
        {
            logCallback($"Error launching silent Docker server process: {ex.Message}", -1);
            return null;
        }
    }
    
    public void OpenDockerWindow()
    {
        try
        {
            // Open Docker Desktop or attach to running container
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/k docker exec -it {ContainerName} /bin/bash";
            process.StartInfo.UseShellExecute = true;
            process.Start();
        }
        catch (Exception ex)
        {
            logCallback($"Error opening Docker window: {ex.Message}", -1);
        }
    }
    #endregion

    #region Docker Control
    
    public void ShutdownDocker()
    {
        if (debugMode) logCallback("Attempting to shut down Docker...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c docker-compose down";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("Docker shutdown command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to shut down Docker: {ex.Message}", -1);
        }
    }

    public void StartDocker()
    {
        if (debugMode) logCallback("Attempting to start Docker...", 0);
        try
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c docker-compose up -d";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            
            if (debugMode) logCallback("Docker startup command issued.", 1);
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error attempting to start Docker: {ex.Message}", -1);
        }    
    }
    #endregion

    #region CheckServerProcess
    public async Task<bool> CheckDockerProcessAsync(bool isDockerRunning)
    {
        if (!isDockerRunning)
        {
            if (debugMode) logCallback("[ServerDockerProcess] Docker is not running, cannot check server process.", 0);
            return false;
        }

        bool processResult = false;
        string errorMessage = null;
        string debugMessage = null;
        bool timedOut = false;
        int exitCode = 0;
            
        try
        {
            var result = await Task.Run<(bool, string, string, bool, int)>(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"ps --filter name={ContainerName} --format \"{{{{.Names}}}}\"";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        bool finished = process.WaitForExit(5000);
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            return (false, "Docker ps command timed out", null, true, -1);
                        }
                        
                        int code = process.ExitCode;
                        
                        if (code == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            bool found = output.Trim().Contains(ContainerName);
                            return (found, null, found ? $"Container {ContainerName} found" : "Container not found", false, code);
                        }
                        else if (code == 0)
                        {
                            return (false, null, "Container not found (empty output)", false, code);
                        }
                        else
                        {
                            return (false, $"Docker ps failed: {error}", null, false, code);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, $"Exception during Docker ps: {ex.Message}", null, false, -1);
                }
            });

            processResult = result.Item1;
            errorMessage = result.Item2;
            debugMessage = result.Item3;
            timedOut = result.Item4;
            exitCode = result.Item5;
        }
        catch (Exception ex)
        {
            errorMessage = $"Outer exception: {ex.Message}";
        }

        lock (statusUpdateLock)
        {
            cachedServerRunningStatus = processResult;
            lastStatusCacheTime = EditorApplication.timeSinceStartup;
        }

        if (debugMode)
        {
            if (!string.IsNullOrEmpty(errorMessage))
            {
                logCallback($"[ServerDockerProcess] Error checking Docker process: {errorMessage}", -1);
            }
            else if (!string.IsNullOrEmpty(debugMessage))
            {
                logCallback($"[ServerDockerProcess] {debugMessage}", processResult ? 1 : 0);
            }
        }

        return processResult;
    }
    #endregion
    
    #region CheckPrereq

    public void CheckPrerequisites(Action<bool, bool, bool, bool> callback)
    {
        Task.Run(() =>
        {
            bool hasDocker = false;
            bool hasDockerCompose = false;
            bool hasDockerImage = false;
            bool hasDockerContainerMounts = false;

            try
            {
                // Step 1: Check if Docker CLI is installed (platform-agnostic)
                // Use 'docker --version' which works even if daemon isn't running
                using (Process dockerVersionCheck = new Process())
                {
                    dockerVersionCheck.StartInfo.FileName = "docker";
                    dockerVersionCheck.StartInfo.Arguments = "--version";
                    dockerVersionCheck.StartInfo.RedirectStandardOutput = true;
                    dockerVersionCheck.StartInfo.RedirectStandardError = true;
                    dockerVersionCheck.StartInfo.UseShellExecute = false;
                    dockerVersionCheck.StartInfo.CreateNoWindow = true;
                    
                    dockerVersionCheck.Start();
                    string output = dockerVersionCheck.StandardOutput.ReadToEnd();
                    string error = dockerVersionCheck.StandardError.ReadToEnd();
                    bool exited = dockerVersionCheck.WaitForExit(5000);
                    
                    if (exited && dockerVersionCheck.ExitCode == 0 && output.Contains("Docker version"))
                    {
                        hasDocker = true;
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Docker is installed: {output.Trim()}");
                        }
                    }
                    else
                    {
                        if (debugMode)
                        {
                            if (!exited)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check timed out - Docker may not be installed");
                            }
                            else if (dockerVersionCheck.ExitCode != 0)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check failed (exit code {dockerVersionCheck.ExitCode})");
                                if (!string.IsNullOrEmpty(error))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {error.Trim()}");
                                }
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker version check succeeded but output doesn't contain version info: {output}");
                            }
                        }
                    }
                    
                    if (!exited)
                    {
                        try { dockerVersionCheck.Kill(); } catch { }
                    }
                }

                // Step 2: If Docker CLI exists, check if daemon is running
                if (hasDocker)
                {
                    using (Process dockerPingCheck = new Process())
                    {
                        dockerPingCheck.StartInfo.FileName = "docker";
                        dockerPingCheck.StartInfo.Arguments = "info";
                        dockerPingCheck.StartInfo.RedirectStandardOutput = true;
                        dockerPingCheck.StartInfo.RedirectStandardError = true;
                        dockerPingCheck.StartInfo.UseShellExecute = false;
                        dockerPingCheck.StartInfo.CreateNoWindow = true;
                        
                        dockerPingCheck.Start();
                        string output = dockerPingCheck.StandardOutput.ReadToEnd();
                        string error = dockerPingCheck.StandardError.ReadToEnd();
                        bool exited = dockerPingCheck.WaitForExit(5000);
                        
                        if (exited && dockerPingCheck.ExitCode == 0)
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] Docker daemon is running");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                string reason = !exited ? "timeout" : $"exit code {dockerPingCheck.ExitCode}";
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker daemon is not running ({reason})");
                                if (!string.IsNullOrEmpty(error) && error.Contains("Cannot connect"))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker Desktop may not be started. Please start Docker Desktop.");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { dockerPingCheck.Kill(); } catch { }
                        }
                    }
                }

                // Step 3: Check if Docker Compose is available (only if Docker CLI exists)
                if (hasDocker)
                {
                    // Check if Docker Compose is available
                    using (Process composeCheck = new Process())
                    {
                        composeCheck.StartInfo.FileName = "docker";
                        composeCheck.StartInfo.Arguments = "compose version";
                        composeCheck.StartInfo.RedirectStandardOutput = true;
                        composeCheck.StartInfo.RedirectStandardError = true;
                        composeCheck.StartInfo.UseShellExecute = false;
                        composeCheck.StartInfo.CreateNoWindow = true;
                        
                        composeCheck.Start();
                        string output = composeCheck.StandardOutput.ReadToEnd();
                        string error = composeCheck.StandardError.ReadToEnd();
                        bool exited = composeCheck.WaitForExit(5000);
                        
                        if (exited && composeCheck.ExitCode == 0)
                        {
                            hasDockerCompose = true;
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] Docker Compose is available: {output.Trim()}");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker Compose check failed");
                                if (!string.IsNullOrEmpty(error))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Error: {error.Trim()}");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { composeCheck.Kill(); } catch { }
                        }
                    }

                    // Step 4: Check if SpacetimeDB image exists locally (only if Docker daemon is running)
                    using (Process imageCheck = new Process())
                    {
                        imageCheck.StartInfo.FileName = "docker";
                        imageCheck.StartInfo.Arguments = $"images -q {ImageName}";
                        imageCheck.StartInfo.RedirectStandardOutput = true;
                        imageCheck.StartInfo.RedirectStandardError = true;
                        imageCheck.StartInfo.UseShellExecute = false;
                        imageCheck.StartInfo.CreateNoWindow = true;
                        
                        imageCheck.Start();
                        string output = imageCheck.StandardOutput.ReadToEnd();
                        string error = imageCheck.StandardError.ReadToEnd();
                        bool exited = imageCheck.WaitForExit(5000);
                        
                        if (exited && imageCheck.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            hasDockerImage = true;
                            if (debugMode)
                            {
                                UnityEngine.Debug.Log($"[ServerDockerProcess] SpacetimeDB image found locally");
                            }
                        }
                        else
                        {
                            if (debugMode)
                            {
                                if (string.IsNullOrWhiteSpace(output))
                                {
                                    UnityEngine.Debug.Log($"[ServerDockerProcess] SpacetimeDB image not found locally (can be pulled when needed)");
                                }
                                if (!string.IsNullOrEmpty(error) && error.Contains("Cannot connect"))
                                {
                                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Cannot check for images - Docker daemon not running");
                                }
                            }
                        }
                        
                        if (!exited)
                        {
                            try { imageCheck.Kill(); } catch { }
                        }
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // This exception occurs when the executable is not found (Docker not in PATH)
                if (debugMode) 
                {
                    UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Docker executable not found. Docker may not be installed or not in system PATH: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                if (debugMode) 
                {
                    UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking prerequisites: {ex.Message}\n{ex.StackTrace}");
                }
            }
            
            // Step 5: Check container mounts if Docker and image are available
            if (hasDocker && hasDockerImage)
            {
                hasDockerContainerMounts = CheckDockerContainerMounts();
            }
            
            if (debugMode) 
            {
                UnityEngine.Debug.Log($"[ServerDockerProcess] Prerequisites check complete - Docker: {hasDocker}, Compose: {hasDockerCompose}, Image: {hasDockerImage}, Container Mounts: {hasDockerContainerMounts}");
            }

            EditorApplication.delayCall += () => callback(hasDocker, hasDockerCompose, hasDockerImage, hasDockerContainerMounts);
        });
    }
    
    /// <summary>
    /// Checks if the Docker container has correct volume mounts for Unity file generation
    /// </summary>
    private bool CheckDockerContainerMounts()
    {
        try
        {
            // Check if container exists
            var (exists, isRunning) = CheckContainerExistsAndRunning();
            
            if (!exists)
            {
                // Container doesn't exist yet - that's okay, it will be created with correct mounts
                return true;
            }
            
            // Inspect the container to check mounts
            using (Process inspectProcess = new Process())
            {
                inspectProcess.StartInfo.FileName = "docker";
                inspectProcess.StartInfo.Arguments = $"inspect {ContainerName}";
                inspectProcess.StartInfo.UseShellExecute = false;
                inspectProcess.StartInfo.RedirectStandardOutput = true;
                inspectProcess.StartInfo.RedirectStandardError = true;
                inspectProcess.StartInfo.CreateNoWindow = true;
                
                inspectProcess.Start();
                string output = inspectProcess.StandardOutput.ReadToEnd();
                inspectProcess.WaitForExit();
                
                if (inspectProcess.ExitCode != 0)
                {
                    return false;
                }
                
                // Check if /unity and /app mounts exist in the output
                bool hasUnityMount = output.Contains("/unity");
                bool hasAppMount = output.Contains("/app");
                
                // For proper configuration, we need at least the Unity mount
                // The /app mount is also desirable but not strictly required for all operations
                bool hasProperMounts = hasUnityMount;
                
                if (debugMode)
                {
                    UnityEngine.Debug.Log($"[ServerDockerProcess] Container mount check - Unity: {hasUnityMount}, App: {hasAppMount}, Proper: {hasProperMounts}");
                }
                
                return hasProperMounts;
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking container mounts: {ex.Message}");
            }
            return false;
        }
    }
    
    /// <summary>
    /// Public wrapper to check if a Docker container exists and if it's running
    /// </summary>
    /// <param name="containerName">Optional container name, defaults to ContainerName constant</param>
    /// <returns>Tuple of (exists, isRunning)</returns>
    public (bool exists, bool isRunning) CheckContainerExistsAndRunning(string containerName = null)
    {
        if (string.IsNullOrEmpty(containerName))
        {
            containerName = ContainerName;
        }
        
        return CheckContainerStatus(containerName);
    }
    #endregion

    #region StopServer
    public async Task<bool> StopServer(string containerName = null)
    {
        string target = containerName ?? ContainerName;
        
        try
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Attempting to stop Docker container: {target}", 0);

            var result = await StopDockerContainer(target);
            
            if (result)
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Successfully stopped container: {target}", 1);
                lock (statusUpdateLock)
                {
                    cachedServerRunningStatus = false;
                }
            }
            else
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Failed to stop container: {target}", -1);
            }

            return result;
        }
        catch (Exception ex)
        {
            logCallback($"[ServerDockerProcess] Exception while stopping server: {ex.Message}", -1);
            return false;
        }
    }
    
    private async Task<bool> StopDockerContainer(string containerName)
    {
        try
        {
            // Invalidate cache immediately
            lock (statusUpdateLock)
            {
                lastStatusCacheTime = 0; // Force fresh check next time
            }
            
            var stopResult = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"stop {containerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Executing: docker stop {containerName}");
                        }
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        bool finished = process.WaitForExit(30000); // 30 second timeout for graceful stop
                        if (!finished)
                        {
                            if (debugMode)
                            {
                                UnityEngine.Debug.LogWarning($"[ServerDockerProcess] Stop command timed out, forcing kill...");
                            }
                            try { process.Kill(); } catch { }
                            return (false, "Stop command timed out");
                        }
                        
                        if (debugMode)
                        {
                            UnityEngine.Debug.Log($"[ServerDockerProcess] Stop command exit code: {process.ExitCode}");
                            if (!string.IsNullOrEmpty(output)) UnityEngine.Debug.Log($"[ServerDockerProcess] Stop output: {output}");
                            if (!string.IsNullOrEmpty(error)) UnityEngine.Debug.Log($"[ServerDockerProcess] Stop error: {error}");
                        }
                        
                        if (process.ExitCode == 0)
                        {
                            return (true, null);
                        }
                        else
                        {
                            return (false, $"Stop failed with exit code {process.ExitCode}: {error}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            });

            if (!stopResult.Item1 && debugMode)
            {
                logCallback($"[ServerDockerProcess] Stop container error: {stopResult.Item2}", -1);
            }

            // Update cache to reflect stopped state
            if (stopResult.Item1)
            {
                lock (statusUpdateLock)
                {
                    cachedServerRunningStatus = false;
                    lastStatusCacheTime = EditorApplication.timeSinceStartup;
                }
                
                // Note: We're NOT removing the container so it can be restarted
                // await RemoveDockerContainer(containerName);
            }

            return stopResult.Item1;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception stopping container: {ex.Message}", -1);
            return false;
        }
    }

    private async Task<bool> RemoveDockerContainer(string containerName)
    {
        try
        {
            var removeResult = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"rm {containerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        process.WaitForExit(5000);
                        
                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });

            if (debugMode) logCallback($"[ServerDockerProcess] Remove container result: {removeResult}", removeResult ? 1 : 0);
            return removeResult;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception removing container: {ex.Message}", -1);
            return false;
        }
    }
    
    public bool CheckIfServerRunningDocker()
    {
        // Use cached status if available and not expired
        double currentTime = EditorApplication.timeSinceStartup;
        lock (statusUpdateLock)
        {
            if (currentTime - lastStatusCacheTime < statusCacheTimeout)
            {
                if (debugMode) logCallback($"[ServerDockerProcess] Using cached status: {cachedServerRunningStatus}", 0);
                return cachedServerRunningStatus;
            }
        }
        
        // Otherwise perform fresh check using CheckContainerStatus
        try
        {
            var (exists, isRunning) = CheckContainerStatus(ContainerName);
            
            lock (statusUpdateLock)
            {
                cachedServerRunningStatus = exists && isRunning;
                lastStatusCacheTime = currentTime;
            }
            
            if (debugMode)
            {
                logCallback($"[ServerDockerProcess] Container status check - Exists: {exists}, Running: {isRunning}", 0);
            }
            
            return exists && isRunning;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error checking if server running: {ex.Message}", -1);
            
            // Invalidate cache on error
            lock (statusUpdateLock)
            {
                lastStatusCacheTime = 0;
            }
            
            return false;
        }
    }
    
    #endregion

    #region RunDockerCommand

    public int RunDockerCommandSilent(string dockerCommand)
    {
        try
        {
            using (Process process = new Process())
            {
                process.StartInfo.FileName = "docker";
                process.StartInfo.Arguments = dockerCommand;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                process.Start();
                process.WaitForExit();

                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error running Docker command: {ex.Message}", -1);
            return -1;
        }
    }
    #endregion
    
    #region RunServerCommand

    public async Task<(string output, string error, bool success)> RunServerCommandAsync(string command, string serverDirectory = null, bool isStatusCheck = false)
    {
        try
        {
            // Execute command inside the running container
            string dockerExecCommand = $"exec {ContainerName} {command}";
            
            if (debugMode && !isStatusCheck) 
            {
                logCallback($"[ServerDockerProcess] Running command in container: {command}", 0);
            }

            // Check if this is a login command that needs real-time output
            bool isLoginCommand = command.Contains("spacetime login") && !command.Contains("show --token");

            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = dockerExecCommand;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;

                        var outputBuilder = new StringBuilder();
                        var errorBuilder = new StringBuilder();

                        process.OutputDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                            {
                                outputBuilder.AppendLine(args.Data);
                                // For login commands, process output to auto-open URLs and log in real-time
                                if (isLoginCommand && logCallback != null)
                                {
                                    string processedOutput = ServerUtilityProvider.ProcessOutputAndOpenUrls(args.Data);
                                    // Use success log level (1) for successful login messages
                                    int logLevel = processedOutput.Contains("Login successful!") ? 1 : 0;
                                    logCallback(processedOutput, logLevel);
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, args) =>
                        {
                            if (args.Data != null)
                            {
                                errorBuilder.AppendLine(args.Data);
                                // For login commands, also log errors in real-time
                                if (isLoginCommand && logCallback != null)
                                {
                                    logCallback(args.Data, -2);
                                }
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        // Use longer timeout for login commands (3 minutes) since they require user interaction
                        int timeoutMs = isLoginCommand ? 180000 : 30000; // 3 minutes for login, 30 seconds for others
                        bool finished = process.WaitForExit(timeoutMs);
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            if (isLoginCommand)
                                return ("", "Login command timed out. Please try again.", false);
                            else
                                return ("", "Command timed out. Please try again.", false);
                        }

                        string output = outputBuilder.ToString();
                        string error = errorBuilder.ToString();
                        bool success = process.ExitCode == 0;

                        return (output, error, success);
                    }
                }
                catch (Exception ex)
                {
                    return ("", ex.Message, false);
                }
            });

            if (debugMode && !isStatusCheck)
            {
                if (!result.Item3)
                {
                    logCallback($"[ServerDockerProcess] Command failed: {result.Item2}", -1);
                }
                else if (!string.IsNullOrEmpty(result.Item1))
                {
                    logCallback($"[ServerDockerProcess] Command output: {result.Item1}", 1);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception running server command: {ex.Message}", -1);
            return ("", ex.Message, false);
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Check if a Docker container exists and if it's running
    /// </summary>
    /// <param name="containerName">Name of the container to check</param>
    /// <returns>Tuple of (exists, isRunning)</returns>
    private (bool exists, bool isRunning) CheckContainerStatus(string containerName)
    {
        try
        {
            // Check if container exists (running or stopped)
            using (Process checkProcess = new Process())
            {
                checkProcess.StartInfo.FileName = "docker";
                checkProcess.StartInfo.Arguments = $"ps -a --filter name={containerName} --format {{{{.Names}}}}";
                checkProcess.StartInfo.UseShellExecute = false;
                checkProcess.StartInfo.RedirectStandardOutput = true;
                checkProcess.StartInfo.CreateNoWindow = true;
                
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd().Trim();
                checkProcess.WaitForExit();
                
                bool exists = !string.IsNullOrWhiteSpace(output) && output == containerName;
                
                if (!exists)
                {
                    return (false, false);
                }
                
                // Container exists, now check if it's running
                using (Process runningCheck = new Process())
                {
                    runningCheck.StartInfo.FileName = "docker";
                    runningCheck.StartInfo.Arguments = $"ps --filter name={containerName} --format {{{{.Names}}}}";
                    runningCheck.StartInfo.UseShellExecute = false;
                    runningCheck.StartInfo.RedirectStandardOutput = true;
                    runningCheck.StartInfo.CreateNoWindow = true;
                    
                    runningCheck.Start();
                    string runningOutput = runningCheck.StandardOutput.ReadToEnd().Trim();
                    runningCheck.WaitForExit();
                    
                    bool isRunning = !string.IsNullOrWhiteSpace(runningOutput) && runningOutput == containerName;
                    
                    return (true, isRunning);
                }
            }
        }
        catch (Exception ex)
        {
            if (debugMode)
            {
                UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking container status: {ex.Message}");
            }
            return (false, false);
        }
    }
    
    private (string executable, string arguments) SplitCommandForProcess(string command)
    {
        // Split command into executable and arguments for Process.Start

        if (command.StartsWith("\""))
        {
            int closingQuoteIndex = command.IndexOf("\"", 1);
            if (closingQuoteIndex > 0)
            {
                string executable = command.Substring(1, closingQuoteIndex - 1);
                if (command.Length > closingQuoteIndex + 1)
                {
                    return (executable, command.Substring(closingQuoteIndex + 2).Trim());
                }
            }
            else
            {
                return (command.Substring(1).Trim(), "");
            }
        }
        else
        {
            int firstSpaceIndex = command.IndexOf(' ');
            if (firstSpaceIndex > 0)
            {
                return (command.Substring(0, firstSpaceIndex), command.Substring(firstSpaceIndex + 1).Trim());
            }
            else
            {
                return (command.Trim(), "");
            }
        }
        
        return (command, "");
    }
    
    /// <summary>
    /// Gets the Unity project root directory from an Assets subdirectory path
    /// </summary>
    /// <param name="assetsPath">Path to a directory within Assets</param>
    /// <returns>Unity project root directory containing Assets folder, or null if not found</returns>
    private string GetUnityProjectRoot(string assetsPath)
    {
        if (string.IsNullOrEmpty(assetsPath))
            return null;
            
        try
        {
            // Normalize the path
            string normalizedPath = assetsPath.Replace('\\', '/');
            
            if (debugMode) logCallback($"[GetUnityProjectRoot] Input path: {normalizedPath}", 0);
            
            // Find the Assets folder in the path
            int assetsIndex = normalizedPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex < 0)
            {
                // Check if path ends with /Assets
                assetsIndex = normalizedPath.LastIndexOf("/Assets", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex >= 0 && assetsIndex + 7 == normalizedPath.Length)
                {
                    // Path ends with /Assets, extract everything before it
                    string projectRoot = normalizedPath.Substring(0, assetsIndex).Replace('/', '\\');
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Found /Assets at end. Project root: {projectRoot}", 0);
                    return projectRoot;
                }
                
                // Try without leading slash
                assetsIndex = normalizedPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIndex < 0)
                {
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Could not find 'Assets' in path", -1);
                    return null;
                }
                
                if (assetsIndex == 0)
                {
                    // Path starts with Assets/ (relative path)
                    // Get current Unity project directory
                    string projectRoot = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
                    if (debugMode) logCallback($"[GetUnityProjectRoot] Path starts with Assets/. Using Unity's dataPath: {projectRoot}", 0);
                    return projectRoot;
                }
            }
            
            // Extract everything before /Assets/ or Assets/
            string result = normalizedPath.Substring(0, assetsIndex).Replace('/', '\\');
            
            // Handle case where path starts with Assets (relative path)
            if (string.IsNullOrEmpty(result))
            {
                // Get current Unity project directory
                result = UnityEngine.Application.dataPath.Replace("/Assets", "").Replace('/', '\\');
            }
            
            if (debugMode) logCallback($"[GetUnityProjectRoot] Final project root: {result}", 0);
            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"Error getting Unity project root: {ex.Message}", -1);
            return null;
        }
    }

    public void PingServer(string serverUrl, Action<bool, string> callback)
    {
        try
        {
            // Run the ping command asynchronously inside the Docker container
            Task.Run(async () =>
            {
                try
                {
                    var result = await RunServerCommandAsync($"spacetime server ping {serverUrl}", null, true);
                    
                    // Combine output and error for parsing
                    string fullOutput = result.output + result.error;
                    
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
                    
                    // Use EditorApplication.delayCall to ensure callback is called on main thread
                    EditorApplication.delayCall += () => callback(isOnline, message);
                }
                catch (Exception ex)
                {
                    if (debugMode) UnityEngine.Debug.LogError($"[ServerDockerProcess] Error pinging server: {ex.Message}");
                    EditorApplication.delayCall += () => callback(false, $"Error pinging server: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            if (debugMode) UnityEngine.Debug.LogError($"[ServerDockerProcess] Error starting ping task: {ex.Message}");
            callback(false, $"Error pinging server: {ex.Message}");
        }
    }
    
    #endregion

    #region Service Management
    
    public async Task<bool> IsDockerServiceRunning()
    {
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = "version";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(5000);
                        
                        // Docker is running if we can get version info and exit code is 0
                        // Also check for "Server:" section which confirms Docker daemon is running
                        bool hasServerInfo = output.Contains("Server:") || output.Contains("Server Version:");
                        return process.ExitCode == 0 && hasServerInfo;
                    }
                }
                catch
                {
                    return false;
                }
            });

            if (debugMode && result) logCallback("[ServerDockerProcess] Docker service is running", 0);
            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error checking Docker service: {ex.Message}", -1);
            return false;
        }
    }

    public async Task<bool> StartDockerService()
    {
        try
        {
            // On Windows, try to start Docker Desktop
            if (debugMode) logCallback("[ServerDockerProcess] Attempting to start Docker Desktop...", 0);
            
            var result = await Task.Run(() =>
            {
                try
                {
                    // Try to start Docker Desktop executable
                    string dockerDesktopPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Docker", "Docker", "Docker Desktop.exe"
                    );

                    if (System.IO.File.Exists(dockerDesktopPath))
                    {
                        Process.Start(dockerDesktopPath);
                        return true;
                    }
                    else
                    {
                        if (debugMode) 
                        {
                            logCallback($"[ServerDockerProcess] Docker Desktop not found at: {dockerDesktopPath}", -1);
                        }
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    if (debugMode) 
                    {
                        logCallback($"[ServerDockerProcess] Error starting Docker Desktop: {ex.Message}", -1);
                    }
                    return false;
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Exception starting Docker service: {ex.Message}", -1);
            return false;
        }
    }

    #endregion

    #region Docker Log Management

    public async Task<string> GetDockerLogs(int tailLines = 100)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    using (Process process = new Process())
                    {
                        process.StartInfo.FileName = "docker";
                        process.StartInfo.Arguments = $"logs --tail {tailLines} {ContainerName}";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
                        process.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit(5000);
                        
                        if (process.ExitCode == 0)
                        {
                            return output + error; // Docker logs can output to both streams
                        }
                        else
                        {
                            return $"Error getting logs: {error}";
                        }
                    }
                }
                catch (Exception ex)
                {
                    return $"Exception getting logs: {ex.Message}";
                }
            });

            return result;
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error getting Docker logs: {ex.Message}", -1);
            return "";
        }
    }

    public async Task<bool> ClearDockerLogs()
    {
        // Docker doesn't have a built-in clear logs command
        // We can restart the container to clear logs
        if (debugMode) logCallback("[ServerDockerProcess] Clearing logs by restarting container...", 0);
        
        bool stopped = await StopServer();
        if (!stopped)
        {
            return false;
        }

        // Container will be recreated on next start with fresh logs
        return true;
    }

    #endregion

    #region Version Checking
    
    public async Task<(string version, string toolVersion, string latestVersion, bool updateAvailable)> CheckSpacetimeDBVersionDocker(bool hasDocker, bool hasDockerImage, string serverDirectory)
    {
        if (debugMode) logCallback("Checking SpacetimeDB version in Docker...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasDocker || !hasDockerImage)
        {
            if (debugMode) logCallback("Skipping SpacetimeDB version check - Docker prerequisites not met", 0);
            return ("", "", "", false);
        }
        
        // Use RunServerCommandAsync to run the spacetime --version command inside the container (mark as status check for silent mode)
        var result = await RunServerCommandAsync("spacetime --version", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) logCallback("Failed to get SpacetimeDB version from Docker", -1);
            return ("", "", "", false);
        }
        
        // Parse the version from output that looks like:
        // "spacetime Path: /root/.local/share/spacetime/bin/1.3.1/spacetimedb-cli
        // Commit: 
        // spacetimedb tool version 1.3.0; spacetimedb-lib version 1.3.0;"
        // Prefer the version from the path (1.3.1) over the tool version (1.3.0)
        string version = "";
        string toolversion = "";

        // First try to extract version from the path (preferred method)
        System.Text.RegularExpressions.Match pathMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"Path:\s+[^\r\n]*?/bin/([0-9]+\.[0-9]+\.[0-9]+)/");

        if (pathMatch.Success && pathMatch.Groups.Count > 1)
        {
            version = pathMatch.Groups[1].Value;
            if (debugMode) logCallback($"Detected SpacetimeDB version from path: {version}", 1);
        }
        else
        {
            // Fallback to tool version if path version not found
            System.Text.RegularExpressions.Match fallbackToolMatch = 
                System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

            if (fallbackToolMatch.Success && fallbackToolMatch.Groups.Count > 1)
            {
                version = fallbackToolMatch.Groups[1].Value;
                if (debugMode) logCallback($"Detected SpacetimeDB version from tool output: {version}", 1);
            }
        }

        // Also save the tool version for cargo.toml version update in Setup Window
        System.Text.RegularExpressions.Match toolMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"spacetimedb tool version ([0-9]+\.[0-9]+\.[0-9]+)");

        if (toolMatch.Success && toolMatch.Groups.Count > 1)
        {
            toolversion = toolMatch.Groups[1].Value;
            if (debugMode) logCallback($"Detected SpacetimeDB tool version from output: {toolversion}", 1);
        }

        if (!string.IsNullOrEmpty(version))
        {
            // Check if update is available by comparing with the latest version
            string latestVersion = CCCPSettingsAdapter.GetSpacetimeDBLatestVersion();
            bool updateAvailable = !string.IsNullOrEmpty(latestVersion) && version != latestVersion;
            
            return (version, toolversion, latestVersion, updateAvailable);
        }
        else
        {
            if (debugMode) logCallback("Could not parse SpacetimeDB version from Docker output", -1);
            return ("", "", "", false);
        }
    }
    
    public async Task<(string rustVersion, string rustLatestVersion, bool rustUpdateAvailable, string rustupVersion, bool rustupUpdateAvailable)> CheckRustVersionDocker(bool hasDocker, bool hasDockerImage, string serverDirectory)
    {
        if (debugMode) logCallback("Checking Rust version in Docker...", 0);
        
        // Only proceed if enough prerequisites are met
        if (!hasDocker || !hasDockerImage)
        {
            if (debugMode) logCallback("Skipping Rust version check - Docker prerequisites not met", 0);
            return ("", "", false, "", false);
        }
        
        // Use RunServerCommandAsync to run the rustup check command inside the container (mark as status check for silent mode)
        var result = await RunServerCommandAsync("rustup check", serverDirectory, true);
        
        if (string.IsNullOrEmpty(result.output))
        {
            if (debugMode) logCallback("Failed to get Rust version information from Docker", -1);
            return ("", "", false, "", false);
        }
        
        if (debugMode) logCallback($"Rust check output: {result.output}", 0);
        
        // Parse the version from output that looks like:
        // "stable-x86_64-unknown-linux-gnu - Up to date : 1.89.0 (29483883e 2025-08-04)
        // rustup - Up to date : 1.28.2"
        // Or when updates are available:
        // "stable-x86_64-unknown-linux-gnu - Update available : 1.89.0 (29483883e 2025-08-04) -> 1.90.0 (5bc8c42bb 2025-09-04)
        // rustup - Update available : 1.28.2 -> 1.29.0"
        
        string rustStableVersion = "";
        string rustLatestVersion = "";
        string rustupCurrentVersion = "";
        bool rustUpdateAvailable = false;
        bool rustupUpdateAvailable = false;
        
        // Parse Rust stable version
        System.Text.RegularExpressions.Match rustMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustMatch.Success && rustMatch.Groups.Count > 1)
        {
            rustStableVersion = rustMatch.Groups[1].Value;
            
            // Check if update is available for Rust and extract latest version
            if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Update available"))
            {
                rustUpdateAvailable = true;
                
                // Try to extract the latest version from "1.89.0 -> 1.90.0" format
                System.Text.RegularExpressions.Match latestMatch = 
                    System.Text.RegularExpressions.Regex.Match(result.output, @"stable-x86_64-unknown-linux-gnu.*?->\s*([0-9]+\.[0-9]+\.[0-9]+)");
                
                if (latestMatch.Success && latestMatch.Groups.Count > 1)
                {
                    rustLatestVersion = latestMatch.Groups[1].Value;
                    if (debugMode) logCallback($"Rust update available from version: {rustStableVersion} to {rustLatestVersion}", 1);
                }
                else
                {
                    if (debugMode) logCallback($"Rust update available from version: {rustStableVersion}", 1);
                }
            }
            else if (result.output.Contains("stable-x86_64-unknown-linux-gnu - Up to date"))
            {
                // Clear the latest version when up to date
                rustLatestVersion = "";
                if (debugMode) logCallback($"Rust is up to date at version: {rustStableVersion}", 1);
            }
        }
        
        // Parse rustup version
        System.Text.RegularExpressions.Match rustupMatch = 
            System.Text.RegularExpressions.Regex.Match(result.output, @"rustup.*?:\s*([0-9]+\.[0-9]+\.[0-9]+)");
        
        if (rustupMatch.Success && rustupMatch.Groups.Count > 1)
        {
            rustupCurrentVersion = rustupMatch.Groups[1].Value;
            
            // Check if update is available for rustup
            if (result.output.Contains("rustup - Update available"))
            {
                rustupUpdateAvailable = true;
                if (debugMode) logCallback($"Rustup update available from version: {rustupCurrentVersion}", 1);
            }
            else if (result.output.Contains("rustup - Up to date"))
            {
                if (debugMode) logCallback($"Rustup is up to date at version: {rustupCurrentVersion}", 1);
            }
        }
        
        if (string.IsNullOrEmpty(rustStableVersion) && string.IsNullOrEmpty(rustupCurrentVersion))
        {
            if (debugMode) logCallback("Could not parse Rust version information from Docker output", -1);
        }
        
        return (rustStableVersion, rustLatestVersion, rustUpdateAvailable, rustupCurrentVersion, rustupUpdateAvailable);
    }
    
    #endregion

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
