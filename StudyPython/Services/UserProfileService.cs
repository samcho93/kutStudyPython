using System.IO;
using System.Text.Json;
using StudyPython.Models;

namespace StudyPython.Services;

public static class UserProfileService
{
    private static readonly string ProfileDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StudyPython");
    private static readonly string ProfilePath = Path.Combine(ProfileDir, "profile.json");

    public static UserProfile Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                var json = File.ReadAllText(ProfilePath);
                return JsonSerializer.Deserialize<UserProfile>(json) ?? new UserProfile();
            }
        }
        catch { }
        return new UserProfile();
    }

    public static void Save(UserProfile profile)
    {
        try
        {
            Directory.CreateDirectory(ProfileDir);
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilePath, json);
        }
        catch { }
    }
}
