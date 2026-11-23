using System.Diagnostics;
using GLauncher.Core.Auth;
using Windows.Management.Deployment;

namespace GLauncher.Core.Bedrock;

public enum BedrockEdition { Release, Preview }

public sealed record BedrockInstallInfo(bool Installed, string? Version);

/// <summary>Проверка установки и запуск Minecraft for Windows (Bedrock) из Microsoft Store.</summary>
public static class BedrockService
{
    private static string FamilyName(BedrockEdition edition) => edition == BedrockEdition.Release
        ? "Microsoft.MinecraftUWP_8wekyb3d8bbwe"
        : "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe";

    private static string LaunchUri(BedrockEdition edition) => edition == BedrockEdition.Release
        ? "minecraft:"
        : "minecraft-preview:";

    public static BedrockInstallInfo GetInstallInfo(BedrockEdition edition)
    {
        try
        {
            var package = new PackageManager().FindPackagesForUser(string.Empty, FamilyName(edition)).FirstOrDefault();
            if (package is not null)
            {
                var v = package.Id.Version;
                return new BedrockInstallInfo(true, $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}");
            }
        }
        catch (Exception)
        {
            // PackageManager недоступен — считаем, что не установлено.
        }
        return new BedrockInstallInfo(false, null);
    }

    public static void Launch(BedrockEdition edition, Account account)
    {
        if (!account.OwnsBedrock)
            throw new LauncherException("На этом аккаунте нет лицензии Minecraft: Bedrock Edition.");
        if (!GetInstallInfo(edition).Installed)
            throw new LauncherException(edition == BedrockEdition.Release
                ? "Minecraft for Windows не установлен. Установите его из Microsoft Store или приложения Xbox."
                : "Minecraft Preview не установлен.");

        Process.Start(new ProcessStartInfo(LaunchUri(edition)) { UseShellExecute = true });
    }
}
