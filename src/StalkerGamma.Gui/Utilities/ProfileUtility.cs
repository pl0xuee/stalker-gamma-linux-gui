using System.IO;

namespace StalkerGamma.Gui.Utilities;

/// <summary>Vendored from stalker-gamma-cli/Utilities/ProfileUtility.cs.</summary>
public static class ProfileUtility
{
    public static string ValidateProfileExists(string gamma)
    {
        if (!Directory.Exists(gamma))
        {
            throw new DirectoryNotFoundException($"Directory {gamma} doesn't exist");
        }

        var gammaProfilesPath = Path.Join(gamma, "profiles");
        if (!Directory.Exists(gammaProfilesPath))
        {
            throw new DirectoryNotFoundException($"Directory {gammaProfilesPath} doesn't exist");
        }

        return gammaProfilesPath;
    }
}
