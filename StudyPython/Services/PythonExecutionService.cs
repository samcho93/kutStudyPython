using System.Diagnostics;
using System.IO;
using System.Text;

namespace StudyPython.Services;

public static class PythonExecutionService
{
    private static Process? _runningProcess;
    private static Process? _replProcess;

    public static string DetectPythonPath()
    {
        var candidates = new[]
        {
            "python",
            "python3",
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python312\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python311\python.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Python\Python310\python.exe"),
        };

        foreach (var path in candidates)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit(3000);
                    if (proc.ExitCode == 0)
                        return path;
                }
            }
            catch { }
        }

        return "python";
    }

    public static async Task<(string Output, string Error, int ExitCode)> RunCodeAsync(
        string pythonCode, string pythonPath = "python", CancellationToken ct = default,
        string? workingDirectory = null)
    {
        StopRunningProcess();

        var tempFile = Path.Combine(Path.GetTempPath(), $"study_python_{Guid.NewGuid():N}.py");
        try
        {
            await File.WriteAllTextAsync(tempFile, pythonCode, Encoding.UTF8, ct);

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u \"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _runningProcess = process;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(ct);

            _runningProcess = null;
            return (outputBuilder.ToString().TrimEnd(), errorBuilder.ToString().TrimEnd(), process.ExitCode);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>실시간 출력 스트리밍으로 코드 실행 (while 루프 등에서 print 실시간 표시)</summary>
    public static async Task<int> RunCodeStreamingAsync(
        string pythonCode, string pythonPath, Action<string> onOutput, Action<string> onError,
        CancellationToken ct, string? workingDirectory = null)
    {
        StopRunningProcess();

        var tempFile = Path.Combine(Path.GetTempPath(), $"study_python_{Guid.NewGuid():N}.py");
        try
        {
            await File.WriteAllTextAsync(tempFile, pythonCode, Encoding.UTF8, ct);

            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u \"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(workingDirectory) && Directory.Exists(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _runningProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutput(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onError(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var registration = ct.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(ct);

            _runningProcess = null;
            return process.ExitCode;
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    public static void StopRunningProcess()
    {
        try
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                _runningProcess.Kill(true);
                _runningProcess = null;
            }
        }
        catch { }
    }

    // REPL 관련
    public static Process? StartReplProcess(string pythonPath, Action<string> onOutput)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-i -u",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _replProcess = process;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutput(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutput(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }
        catch
        {
            return null;
        }
    }

    public static void SendReplInput(string input)
    {
        try
        {
            if (_replProcess != null && !_replProcess.HasExited)
            {
                _replProcess.StandardInput.WriteLine(input);
                _replProcess.StandardInput.Flush();
            }
        }
        catch { }
    }

    public static void StopReplProcess()
    {
        try
        {
            if (_replProcess != null && !_replProcess.HasExited)
            {
                _replProcess.Kill(true);
                _replProcess = null;
            }
        }
        catch { }
    }
}
