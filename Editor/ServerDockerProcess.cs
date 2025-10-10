using UnityEngine;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
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
    public const string ImageName = "spacetimedb/spacetimedb";
    
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

    public Process StartVisibleServerProcess(string serverDirectory)
    {
        try 
        {
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe";
            
            // Start Docker container with interactive mode
            string dockerCommand = $"docker run -it --rm --name {ContainerName} -p 3000:3000 -v \"{serverDirectory}:/app\" {ImageName}";
            
            process.StartInfo.Arguments = $"/k {dockerCommand}";
            process.StartInfo.UseShellExecute = true;
            
            if (debugMode) logCallback("Starting SpacetimeDB Server (Docker Visible CMD)...", 0);
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
    
    public Process StartSilentServerProcess(string serverDirectory)
    {
        try 
        {
            // Start Docker container in detached mode
            string dockerCommand = $"docker run -d --name {ContainerName} -p 3000:3000 -v \"{serverDirectory}:/app\" {ImageName}";
            
            if (debugMode) logCallback($"Docker command: {dockerCommand}", 0);
            
            Process process = new Process();
            process.StartInfo.FileName = "cmd.exe"; 
            process.StartInfo.Arguments = $"/c {dockerCommand}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            
            if (debugMode) logCallback("Attempting to launch Docker server process...", 0);
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

    public void CheckPrerequisites(Action<bool, bool, bool> callback)
    {
        Task.Run(() =>
        {
            bool hasDocker = false;
            bool hasDockerCompose = false;
            bool hasDockerImage = false;

            try
            {
                // Check if Docker is installed
                using (Process dockerCheck = new Process())
                {
                    dockerCheck.StartInfo.FileName = "docker";
                    dockerCheck.StartInfo.Arguments = "--version";
                    dockerCheck.StartInfo.RedirectStandardOutput = true;
                    dockerCheck.StartInfo.RedirectStandardError = true;
                    dockerCheck.StartInfo.UseShellExecute = false;
                    dockerCheck.StartInfo.CreateNoWindow = true;
                    
                    dockerCheck.Start();
                    dockerCheck.WaitForExit(3000);
                    
                    if (dockerCheck.ExitCode == 0)
                    {
                        hasDocker = true;
                    }
                }

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
                        composeCheck.WaitForExit(3000);
                        
                        if (composeCheck.ExitCode == 0)
                        {
                            hasDockerCompose = true;
                        }
                    }

                    // Check if SpacetimeDB image exists locally
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
                        imageCheck.WaitForExit(3000);
                        
                        if (imageCheck.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        {
                            hasDockerImage = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (debugMode) 
                {
                    UnityEngine.Debug.LogError($"[ServerDockerProcess] Error checking prerequisites: {ex.Message}");
                }
            }
            
            if (debugMode) 
            {
                UnityEngine.Debug.Log($"[ServerDockerProcess] Prerequisites check - Docker: {hasDocker}, Compose: {hasDockerCompose}, Image: {hasDockerImage}");
            }

            EditorApplication.delayCall += () => callback(hasDocker, hasDockerCompose, hasDockerImage);
        });
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
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        
                        bool finished = process.WaitForExit(15000); // 15 second timeout
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            return (false, "Stop command timed out");
                        }
                        
                        if (process.ExitCode == 0)
                        {
                            return (true, null);
                        }
                        else
                        {
                            return (false, $"Stop failed: {error}");
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

            // Also remove the container
            if (stopResult.Item1)
            {
                await RemoveDockerContainer(containerName);
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
        
        // Otherwise perform fresh check synchronously
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
                process.WaitForExit(3000);
                
                bool isRunning = process.ExitCode == 0 && output.Trim().Contains(ContainerName);
                
                lock (statusUpdateLock)
                {
                    cachedServerRunningStatus = isRunning;
                    lastStatusCacheTime = currentTime;
                }
                
                return isRunning;
            }
        }
        catch (Exception ex)
        {
            if (debugMode) logCallback($"[ServerDockerProcess] Error checking if server running: {ex.Message}", -1);
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
                            if (args.Data != null) outputBuilder.AppendLine(args.Data);
                        };

                        process.ErrorDataReceived += (sender, args) =>
                        {
                            if (args.Data != null) errorBuilder.AppendLine(args.Data);
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        bool finished = process.WaitForExit(30000); // 30 second timeout
                        if (!finished)
                        {
                            try { process.Kill(); } catch { }
                            return ("", "Command timed out", false);
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
                        process.StartInfo.Arguments = "info";
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        process.WaitForExit(3000);
                        
                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });

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

} // Class
} // Namespace

// made by Mathias Toivonen at Northern Rogue Games
