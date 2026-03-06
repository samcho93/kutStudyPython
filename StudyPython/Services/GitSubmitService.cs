using System.Diagnostics;
using System.IO;
using System.Text;

namespace StudyPython.Services;

public static class GitSubmitService
{
    private const string RepoUrl = "https://github.com/samcho93/kutPython.git";

    private static string GetPat()
    {
        // 실행파일 옆 pat.txt에서 토큰 읽기
        var patFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pat.txt");
        if (File.Exists(patFile))
            return File.ReadAllText(patFile).Trim();
        return string.Empty;
    }

    private static string Pat => GetPat();

    public static async Task<string> SubmitAsync(string pdfFilePath, Action<string>? onProgress = null)
    {
        var log = new StringBuilder();
        var branchName = DateTime.Now.ToString("yyyy-MM-dd");
        var tempDir = Path.Combine(Path.GetTempPath(), $"StudyPython_submit_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);
            var authUrl = RepoUrl.Replace("https://", $"https://{Pat}@");

            // Clone
            onProgress?.Invoke("저장소 클론 중...");
            var (cloneOut, cloneErr, cloneCode) = await RunGitAsync($"clone \"{authUrl}\" \"{tempDir}\"");
            log.AppendLine($"[Clone] {cloneOut} {cloneErr}");

            if (cloneCode != 0)
                return $"Clone 실패: {cloneErr}";

            // Branch 확인/생성
            onProgress?.Invoke($"브랜치 '{branchName}' 설정 중...");
            var (branchOut, _, _) = await RunGitAsync($"branch -a", tempDir);

            if (branchOut.Contains($"remotes/origin/{branchName}"))
            {
                await RunGitAsync($"checkout {branchName}", tempDir);
            }
            else
            {
                await RunGitAsync($"checkout -b {branchName}", tempDir);
            }

            // PDF 복사
            onProgress?.Invoke("파일 복사 중...");
            var destFile = Path.Combine(tempDir, Path.GetFileName(pdfFilePath));
            File.Copy(pdfFilePath, destFile, true);

            // Git config (임시)
            await RunGitAsync("config user.email \"student@kut.ac.kr\"", tempDir);
            await RunGitAsync("config user.name \"student\"", tempDir);

            // Add, Commit, Push
            onProgress?.Invoke("커밋 중...");
            await RunGitAsync($"add \"{Path.GetFileName(pdfFilePath)}\"", tempDir);

            var commitMsg = $"제출: {Path.GetFileName(pdfFilePath)} ({DateTime.Now:yyyy-MM-dd HH:mm})";
            var (_, commitErr, commitCode) = await RunGitAsync($"commit -m \"{commitMsg}\"", tempDir);

            if (commitCode != 0 && !commitErr.Contains("nothing to commit"))
            {
                log.AppendLine($"[Commit] {commitErr}");
                return $"커밋 실패: {commitErr}";
            }

            onProgress?.Invoke("Push 중...");
            var (pushOut, pushErr, pushCode) = await RunGitAsync($"push -u origin {branchName}", tempDir);
            log.AppendLine($"[Push] {pushOut} {pushErr}");

            if (pushCode != 0)
                return $"Push 실패: {pushErr}";

            onProgress?.Invoke("제출 완료!");
            return $"제출 성공!\n브랜치: {branchName}\n파일: {Path.GetFileName(pdfFilePath)}";
        }
        catch (Exception ex)
        {
            return $"제출 오류: {ex.Message}";
        }
        finally
        {
            // 임시 폴더 정리
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }
    }

    private static async Task<(string Output, string Error, int ExitCode)> RunGitAsync(
        string arguments, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return (output.ToString().TrimEnd(), error.ToString().TrimEnd(), process.ExitCode);
    }
}
