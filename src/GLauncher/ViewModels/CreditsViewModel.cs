using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GLauncher.Core.Players;

namespace GLauncher.ViewModels;

/// <summary>Один человек в «Благодарностях»: роль задана в коде, ник и лицо скина подтягиваются по UUID.</summary>
public partial class CreditEntry(string role, string uuid) : ObservableObject
{
    public string Role { get; } = role;
    public string Uuid { get; } = uuid;

    [ObservableProperty] private string name = "…";
    [ObservableProperty] private ImageSource? face;

    public void Apply(PlayerProfile profile)
    {
        Name = profile.Name;
        Face = profile.SkinPath is null ? null : SkinFace.Load(profile.SkinPath);
    }
}

public partial class CreditsViewModel : ObservableObject
{
    /// <summary>Люди из credits.json (список на GitHub, меняется без релиза).</summary>
    public ObservableCollection<CreditEntry> People { get; } = [];

    public CreditsViewModel() => Rebuild();

    /// <summary>Пересобрать список из CreditsService; ники и лица — сразу из кэша (работает и офлайн).</summary>
    private void Rebuild()
    {
        People.Clear();
        foreach (var p in CreditsService.People)
        {
            var entry = new CreditEntry(p.Role, p.Uuid);
            if (PlayerProfileService.LoadCached(p.Uuid) is { } cached) entry.Apply(cached);
            People.Add(entry);
        }
    }

    /// <summary>Скачивает свежий credits.json, затем заново узнаёт ники и скины: игроки могли их сменить.</summary>
    public async Task RefreshAsync()
    {
        await CreditsService.RefreshAsync(CancellationToken.None);
        Rebuild();
        await Task.WhenAll(People.Select(async person =>
        {
            try
            {
                person.Apply(await PlayerProfileService.FetchAsync(person.Uuid, CancellationToken.None));
            }
            catch
            {
                // Нет сети или Mojang недоступен — остаётся версия из кэша.
            }
        }));
    }
}

/// <summary>Лицо из файла скина: голова (8,8)–(16,16) и поверх неё слой шляпы (40,8)–(48,16).</summary>
public static class SkinFace
{
    public static ImageSource? Load(string skinPath)
    {
        try
        {
            var image = new BitmapImage();
            using (var stream = File.OpenRead(skinPath))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            var skin = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            if (skin.PixelWidth < 64 || skin.PixelHeight < 16) return null;

            var face = new byte[8 * 8 * 4];
            var hat = new byte[8 * 8 * 4];
            skin.CopyPixels(new System.Windows.Int32Rect(8, 8, 8, 8), face, 8 * 4, 0);
            skin.CopyPixels(new System.Windows.Int32Rect(40, 8, 8, 8), hat, 8 * 4, 0);

            for (var i = 0; i < face.Length; i += 4)
            {
                var a = hat[i + 3] / 255.0;
                for (var c = 0; c < 3; c++) face[i + c] = (byte)(hat[i + c] * a + face[i + c] * (1 - a));
                face[i + 3] = 255;
            }

            var result = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, face, 8 * 4);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }
}
