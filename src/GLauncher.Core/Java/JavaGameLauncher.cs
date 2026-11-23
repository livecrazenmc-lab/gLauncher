using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GLauncher.Core.Auth;

namespace GLauncher.Core.Java;

/// <summary>Полный цикл запуска Java Edition: модлоадер → файлы → Java → аргументы → процесс.</summary>
public sealed class JavaGameLauncher(LauncherSettings settings)
{
    public event Action<string>? Log;

    /// <summary>
    /// Запускает игру. Аккаунт должен быть уже проверен (токен обновлён).
    /// Может изменить profile.InstalledVersionId/LoaderVersion — сохраните профили после вызова.
    /// </summary>
    public async Task<Process> LaunchAsync(Profile profile, Account account, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (!account.OwnsJava || account.Uuid is null || account.Username is null)
            throw new LauncherException("На этом аккаунте нет лицензии Minecraft: Java Edition.");
        if (!account.HasValidMinecraftToken)
            throw new LauncherException("Токен аккаунта истёк. Обновите аккаунт.");
        if (string.IsNullOrWhiteSpace(profile.MinecraftVersion))
            throw new LauncherException("В профиле не выбрана версия Minecraft.");

        var versionId = await EnsureVersionInstalledAsync(profile, progress, ct);
        var gameDir = profile.ResolveGameDir();
        Directory.CreateDirectory(gameDir);

        var game = await GameInstaller.PrepareAsync(versionId, gameDir, progress, ct);
        var java = string.IsNullOrWhiteSpace(profile.JavaPath)
            ? await JavaRuntimeManager.EnsureAsync(game.JavaComponent, progress, ct)
            : profile.JavaPath;
        if (!File.Exists(java))
            throw new LauncherException($"Java не найдена: {java}");

        var args = LaunchArguments.Build(game, profile, account, settings, gameDir);

        progress?.Report(new("Запуск игры", 0, 0));
        var psi = new ProcessStartInfo(java)
        {
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        Log?.Invoke($"[gLauncher] Java: {java}");
        Log?.Invoke($"[gLauncher] Версия: {versionId}, папка игры: {gameDir}");

        var parser = new Log4jLineParser();
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => Emit(parser, e.Data);
        proc.ErrorDataReceived += (_, e) => Emit(parser, e.Data);
        proc.Exited += (_, _) => Log?.Invoke($"[gLauncher] Игра завершилась с кодом {proc.ExitCode}");
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return proc;
    }

    private void Emit(Log4jLineParser parser, string? line)
    {
        if (line is null) return;
        lock (parser)
        {
            if (parser.Parse(line) is { } text) Log?.Invoke(text);
        }
    }

    private async Task<string> EnsureVersionInstalledAsync(Profile profile, IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        if (profile.Loader == ModLoader.Vanilla)
            return profile.MinecraftVersion;

        if (profile.InstalledVersionId is { } installed && File.Exists(Paths.VersionJson(installed)))
            return installed;

        string id, loaderVersion;
        if (profile.Loader == ModLoader.Fabric)
        {
            progress?.Report(new("Установка Fabric", 0, 0));
            (id, loaderVersion) = await FabricInstaller.InstallAsync(profile.MinecraftVersion, profile.LoaderVersion, ct);
        }
        else
        {
            // Установщику Forge нужна Java — берём ту, что требует сама версия Minecraft.
            var vanilla = await VersionRepository.GetVersionJsonAsync(profile.MinecraftVersion, ct);
            var component = vanilla["javaVersion"]?["component"].Str() ?? "jre-legacy";
            var java = string.IsNullOrWhiteSpace(profile.JavaPath)
                ? await JavaRuntimeManager.EnsureAsync(component, progress, ct)
                : profile.JavaPath;

            progress?.Report(new("Установка Forge (может занять несколько минут)", 0, 0));
            (id, loaderVersion) = await ForgeInstaller.InstallAsync(profile.MinecraftVersion, profile.LoaderVersion, java,
                line => Log?.Invoke(line), ct);
        }

        profile.InstalledVersionId = id;
        profile.LoaderVersion = loaderVersion;
        return id;
    }
}

public static class LaunchArguments
{
    private static readonly Regex Placeholder = new(@"\$\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    public static List<string> Build(PreparedGame game, Profile profile, Account account, LauncherSettings settings, string gameDir)
    {
        var json = game.Json;
        var customResolution = profile.Width is > 0 && profile.Height is > 0;
        var features = new Dictionary<string, bool>
        {
            ["has_custom_resolution"] = customResolution,
            ["is_demo_user"] = false,
        };

        var vars = new Dictionary<string, string>
        {
            ["auth_player_name"] = account.Username!,
            ["auth_uuid"] = account.Uuid!,
            ["auth_access_token"] = account.MinecraftAccessToken!,
            ["auth_session"] = $"token:{account.MinecraftAccessToken}:{account.Uuid}",
            ["auth_xuid"] = account.Xuid ?? "0",
            ["user_type"] = "msa",
            ["user_properties"] = "{}",
            ["clientid"] = settings.ClientToken,
            ["version_name"] = game.VersionId,
            ["version_type"] = json["type"].Str() ?? "release",
            ["game_directory"] = gameDir,
            ["assets_root"] = Paths.Assets,
            ["assets_index_name"] = game.AssetIndexId,
            ["game_assets"] = game.GameAssetsDir ?? Paths.Assets,
            ["natives_directory"] = game.NativesDir,
            ["library_directory"] = Paths.Libraries,
            ["classpath"] = string.Join(Path.PathSeparator, game.Classpath),
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["launcher_name"] = "gLauncher",
            ["launcher_version"] = "0.1",
            ["resolution_width"] = (profile.Width ?? 854).ToString(),
            ["resolution_height"] = (profile.Height ?? 480).ToString(),
        };

        var args = new List<string> { $"-Xmx{Math.Max(profile.MemoryMb, 512)}M" };
        args.AddRange(SplitCommandLine(profile.JvmArgs));

        if (json["arguments"]?["jvm"] is JsonArray jvm)
            Append(args, jvm, features);
        else
            args.AddRange(["-Djava.library.path=${natives_directory}", "-cp", "${classpath}"]);

        if (game.LoggingArgument is not null) args.Add(game.LoggingArgument);

        args.Add(json["mainClass"].Str() ?? throw new LauncherException("В описании версии нет mainClass."));

        if (json["arguments"]?["game"] is JsonArray gameArgs)
        {
            Append(args, gameArgs, features);
        }
        else if (json["minecraftArguments"].Str() is { } legacy)
        {
            args.AddRange(legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (customResolution)
                args.AddRange(["--width", "${resolution_width}", "--height", "${resolution_height}"]);
        }

        return args.Select(a => Placeholder.Replace(a, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value)).ToList();
    }

    private static void Append(List<string> target, JsonArray source, IReadOnlyDictionary<string, bool> features)
    {
        foreach (var item in source)
        {
            if (item is JsonValue)
            {
                target.Add(item.Str()!);
                continue;
            }
            if (item is null || !Rules.Allowed(item["rules"], features)) continue;

            switch (item["value"])
            {
                case JsonArray values:
                    target.AddRange(values.Select(v => v.Str()).OfType<string>());
                    break;
                case JsonValue value:
                    target.Add(value.Str()!);
                    break;
            }
        }
    }

    /// <summary>Разбивает строку аргументов JVM с учётом кавычек.</summary>
    public static List<string> SplitCommandLine(string? line)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) return result;

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in line)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }
}

/// <summary>Превращает XML-лог log4j (формат официального лаунчера) в читаемые строки.</summary>
public sealed class Log4jLineParser
{
    private static readonly Regex LevelRegex = new("level=\"(\\w+)\"", RegexOptions.Compiled);
    private string _level = "INFO";

    public string? Parse(string line)
    {
        var t = line.Trim();
        if (t.StartsWith("<log4j:Event"))
        {
            var m = LevelRegex.Match(t);
            if (m.Success) _level = m.Groups[1].Value;
            return null;
        }
        if (t.StartsWith("</log4j:Event") || t == "<log4j:Throwable>" || t == "</log4j:Throwable>")
            return null;

        var isMessageStart = t.StartsWith("<log4j:Message>");
        var text = line
            .Replace("<log4j:Message><![CDATA[", "")
            .Replace("<log4j:Throwable><![CDATA[", "")
            .Replace("]]></log4j:Message>", "")
            .Replace("]]></log4j:Throwable>", "");
        if (string.IsNullOrWhiteSpace(text)) return null;
        return isMessageStart ? $"[{_level}] {text.Trim()}" : text;
    }
}
