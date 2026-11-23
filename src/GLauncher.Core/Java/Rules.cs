using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GLauncher.Core.Java;

/// <summary>Правила (rules) из JSON версий Minecraft: ОС, архитектура, фичи.</summary>
public static class Rules
{
    public static string OsArch => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.Arm64 => "arm64",
        _ => "x86",
    };

    public static bool Allowed(JsonNode? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is not JsonArray list || list.Count == 0) return true;

        var allowed = false;
        foreach (var rule in list)
        {
            if (rule is null) continue;
            var match = true;

            if (rule["os"] is JsonObject os)
            {
                if (os["name"].Str() is { } name && name != "windows") match = false;
                if (os["arch"].Str() is { } arch && arch != OsArch) match = false;
                if (os["version"].Str() is { } version &&
                    !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version)) match = false;
            }

            if (rule["features"] is JsonObject required)
            {
                foreach (var (key, value) in required)
                {
                    var have = features?.GetValueOrDefault(key) ?? false;
                    if (value.Bool() != have) match = false;
                }
            }

            if (match) allowed = rule["action"].Str() == "allow";
        }
        return allowed;
    }
}

public static class Maven
{
    /// <summary>«group:artifact:version[:classifier][@ext]» → относительный путь в репозитории.</summary>
    public static string ToPath(string name)
    {
        var ext = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            ext = name[(at + 1)..];
            name = name[..at];
        }
        var p = name.Split(':');
        var classifier = p.Length > 3 ? "-" + p[3] : "";
        return $"{p[0].Replace('.', '/')}/{p[1]}/{p[2]}/{p[1]}-{p[2]}{classifier}.{ext}";
    }

    /// <summary>Ключ библиотеки без версии — для удаления дублей при наследовании версий.</summary>
    public static string Key(string name)
    {
        var at = name.IndexOf('@');
        if (at >= 0) name = name[..at];
        var p = name.Split(':');
        return p.Length > 3 ? $"{p[0]}:{p[1]}:{p[3]}" : $"{p[0]}:{p[1]}";
    }
}
