using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLauncher.Core;
using GLauncher.Core.Skins;
using GLauncher.Skins;
using Microsoft.Win32;

namespace GLauncher.ViewModels;

/// <summary>Скин из библиотеки.</summary>
public partial class SkinItemViewModel : ObservableObject
{
    private readonly SkinsViewModel _owner;

    public SkinItemViewModel(SkinEntry entry, SkinsViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        name = entry.Name;
        isSlim = entry.Model == SkinModel.Slim;
        Thumbnail = MakeThumbnail();
    }

    public SkinEntry Entry { get; }
    public string FilePath => SkinLibrary.PathOf(Entry);

    [ObservableProperty] private string name;
    [ObservableProperty] private bool isSlim;
    [ObservableProperty] private ImageSource? thumbnail;

    partial void OnNameChanged(string value)
    {
        Entry.Name = string.IsNullOrWhiteSpace(value) ? "Скин" : value.Trim();
        _owner.SaveLibrary();
    }

    partial void OnIsSlimChanged(bool value)
    {
        Entry.Model = value ? SkinModel.Slim : SkinModel.Classic;
        Thumbnail = MakeThumbnail();
        _owner.SaveLibrary();
        _owner.OnItemModelChanged(this);
    }

    private ImageSource? MakeThumbnail()
    {
        try { return SkinImage.FromFile(FilePath).FrontView(IsSlim); }
        catch { return null; }
    }
}

/// <summary>Плащ аккаунта (или «Без плаща», если Cape == null).</summary>
public partial class CapeItemViewModel(AccountCape? cape, ImageSource? preview, byte[]? png) : ObservableObject
{
    public AccountCape? Cape { get; } = cape;
    public string Title => Cape?.Alias ?? "Без плаща";
    public ImageSource? Preview { get; } = preview;
    public byte[]? Png { get; } = png;

    [ObservableProperty] private bool isActive;
}

public partial class SkinsViewModel : ObservableObject
{
    private readonly AccountsViewModel _accounts;
    private readonly SkinLibrary _library = SkinLibrary.Load();

    public SkinsViewModel(AccountsViewModel accounts)
    {
        _accounts = accounts;
        foreach (var entry in _library.Skins) Library.Add(new SkinItemViewModel(entry, this));
        Selected = Library.FirstOrDefault();
        _lastAccountId = _accounts.Selected?.Id;
        // Событие приходит и при каждом обновлении токена — перезагружаем только при смене самого аккаунта,
        // иначе загрузка скинов (которая тоже обновляет токен) зациклится.
        _accounts.ActiveAccountChanged += (_, _) =>
        {
            if (_accounts.Selected?.Id == _lastAccountId) return;
            _lastAccountId = _accounts.Selected?.Id;
            ClearAccount();
            if (_accounts.Selected?.OwnsJava == true) _ = RefreshAccountAsync();
        };
    }

    private string? _lastAccountId;

    private const string NoAccountText = "Войдите в аккаунт с Java Edition, чтобы менять скин и плащ";

    private void ClearAccount()
    {
        AccountTextures = null;
        AccountSkinPreview = null;
        AccountInfo = NoAccountText;
        Capes.Clear();
        PreviewCape = null;
    }

    public ObservableCollection<SkinItemViewModel> Library { get; } = [];
    public ObservableCollection<CapeItemViewModel> Capes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(ApplyCommand))]
    private SkinItemViewModel? selected;

    [ObservableProperty] private string nickname = "";
    [ObservableProperty] private string? status;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ResetCommand), nameof(ImportNicknameCommand), nameof(SaveAccountSkinCommand))]
    private bool isBusy;

    // --- Аккаунт ---
    [ObservableProperty] private string accountInfo = "Войдите в аккаунт с Java Edition, чтобы менять скин и плащ";
    [ObservableProperty] private ImageSource? accountSkinPreview;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAccountSkinCommand))]
    private AccountTextures? accountTextures;

    // --- 3D-превью: выбранный скин из библиотеки + активный (или выбранный) плащ ---
    public string? PreviewPath => Selected?.FilePath;
    public bool PreviewSlim => Selected?.IsSlim == true;
    [ObservableProperty] private byte[]? previewCape;

    partial void OnSelectedChanged(SkinItemViewModel? value)
    {
        OnPropertyChanged(nameof(PreviewPath));
        OnPropertyChanged(nameof(PreviewSlim));
    }

    internal void OnItemModelChanged(SkinItemViewModel item)
    {
        if (item == Selected) OnPropertyChanged(nameof(PreviewSlim));
    }

    internal void SaveLibrary() => _library.Save();

    // ===================== Библиотека =====================

    [RelayCommand]
    private void AddFile()
    {
        var dialog = new OpenFileDialog { Filter = "Скин Minecraft (*.png)|*.png", Title = "Добавить скин", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var entry = _library.AddFile(file, SkinModel.Classic);
                var item = new SkinItemViewModel(entry, this);
                Library.Insert(0, item);
                Selected = item;
                Status = $"Добавлен скин «{entry.Name}». Если у скина тонкие руки — отметьте «Тонкие руки».";
            }
            catch (Exception ex)
            {
                Status = $"{Path.GetFileName(file)}: {ex.Message}";
            }
        }
    }

    private bool NotBusy() => !IsBusy;

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ImportNicknameAsync()
    {
        if (string.IsNullOrWhiteSpace(Nickname)) { Status = "Введите ник игрока."; return; }
        await RunAsync($"Ищу скин игрока {Nickname.Trim()}…", async ct =>
        {
            var entry = await _library.ImportFromPlayerAsync(Nickname, ct);
            var item = new SkinItemViewModel(entry, this);
            Library.Insert(0, item);
            Selected = item;
            Nickname = "";
            Status = $"Скин игрока «{entry.Name}» добавлен в библиотеку.";
        });
    }

    private bool HasSelection() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Remove()
    {
        if (Selected is null) return;
        if (MessageBox.Show($"Удалить «{Selected.Name}» из библиотеки?", "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        var index = Library.IndexOf(Selected);
        _library.Remove(Selected.Entry);
        Library.Remove(Selected);
        Selected = Library.Count == 0 ? null : Library[Math.Min(index, Library.Count - 1)];
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(SkinLibrary.Folder);
        Shell.Open(SkinLibrary.Folder);
    }

    // ===================== Аккаунт =====================

    private bool CanApply() => Selected is not null && !IsBusy;

    /// <summary>Установить выбранный скин на аккаунт Java Edition.</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        var item = Selected!;
        await RunAsync("Устанавливаю скин…", async ct =>
        {
            var token = await TokenAsync(ct);
            await SkinApi.UploadAsync(token, await File.ReadAllBytesAsync(item.FilePath, ct), item.Entry.Model, ct);
            Status = $"Скин «{item.Name}» установлен. В игре он появится после перезахода на сервер или в мир.";
            await LoadAccountAsync(token, ct);
        });
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task ResetAsync()
    {
        if (MessageBox.Show("Вернуть стандартный скин на аккаунте?", "gLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        await RunAsync("Сбрасываю скин…", async ct =>
        {
            var token = await TokenAsync(ct);
            await SkinApi.ResetAsync(token, ct);
            Status = "На аккаунте снова стандартный скин.";
            await LoadAccountAsync(token, ct);
        });
    }

    private bool CanSaveAccountSkin() => AccountTextures?.ActiveSkin is not null && !IsBusy;

    /// <summary>Сохранить текущий скин аккаунта в библиотеку.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveAccountSkin))]
    private async Task SaveAccountSkinAsync()
    {
        var skin = AccountTextures!.ActiveSkin!;
        await RunAsync("Сохраняю скин аккаунта…", async ct =>
        {
            var png = await SkinApi.DownloadTextureAsync(skin.Url, ct);
            var entry = _library.AddBytes(png, _accounts.Selected?.Username ?? "Мой скин", skin.Model);
            var item = new SkinItemViewModel(entry, this);
            Library.Insert(0, item);
            Selected = item;
            Status = "Скин аккаунта сохранён в библиотеку.";
        });
    }

    [RelayCommand]
    private async Task SelectCapeAsync(CapeItemViewModel? cape)
    {
        if (cape is null || IsBusy || cape.IsActive) return;
        await RunAsync(cape.Cape is null ? "Снимаю плащ…" : $"Надеваю плащ «{cape.Title}»…", async ct =>
        {
            var token = await TokenAsync(ct);
            await SkinApi.SetCapeAsync(token, cape.Cape?.Id, ct);
            foreach (var c in Capes) c.IsActive = c == cape;
            PreviewCape = cape.Png;
            Status = cape.Cape is null ? "Плащ снят." : $"Плащ «{cape.Title}» надет.";
        });
    }

    [RelayCommand]
    private Task RefreshAccountAsync() => RunAsync("Загружаю скин и плащи аккаунта…", async ct =>
    {
        await LoadAccountAsync(await TokenAsync(ct), ct);
        Status = null;
    }, quietErrors: true);

    /// <summary>Вызывается при запуске лаунчера: показывает текущий скин и плащи, если аккаунт уже вошёл.</summary>
    public Task InitializeAsync() => _accounts.Selected?.OwnsJava == true ? RefreshAccountAsync() : Task.CompletedTask;

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        var account = await _accounts.EnsureActiveValidAsync(ct);
        if (!account.OwnsJava || account.MinecraftAccessToken is null)
            throw new LauncherException("Скины есть только у аккаунтов с Minecraft: Java Edition.");
        return account.MinecraftAccessToken;
    }

    private async Task LoadAccountAsync(string token, CancellationToken ct)
    {
        var textures = await SkinApi.GetAsync(token, ct);
        AccountTextures = textures;
        AccountInfo = _accounts.Selected?.Username ?? "";

        AccountSkinPreview = null;
        if (textures.ActiveSkin is { } skin)
        {
            var png = await SkinApi.DownloadTextureAsync(skin.Url, ct);
            AccountSkinPreview = SkinImage.FromPng(png).FrontView(skin.Model == SkinModel.Slim);
        }

        Capes.Clear();
        var none = new CapeItemViewModel(null, null, null) { IsActive = textures.ActiveCape is null };
        Capes.Add(none);
        foreach (var cape in textures.Capes)
        {
            byte[]? png = null;
            ImageSource? preview = null;
            try
            {
                png = await SkinApi.DownloadTextureAsync(cape.Url, ct);
                preview = CapeFront(png);
            }
            catch (Exception) { /* без картинки, но выбрать можно */ }
            Capes.Add(new CapeItemViewModel(cape, preview, png) { IsActive = cape.Active });
        }
        PreviewCape = Capes.FirstOrDefault(c => c.IsActive)?.Png;
    }

    /// <summary>Лицевая сторона плаща (10×16 из развёртки 64×32) для списка плащей.</summary>
    private static ImageSource? CapeFront(byte[] png)
    {
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(new MemoryStream(png),
            System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        var bmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var k = bmp.PixelWidth / 64;
        if (k < 1) return null;
        var px = new uint[10 * k * 16 * k];
        bmp.CopyPixels(new Int32Rect(k, k, 10 * k, 16 * k), px, 10 * k * 4, 0);
        return SkinImage.Upscale(px, 10 * k, 16 * k, Math.Max(1, 4 / k));
    }

    private async Task RunAsync(string message, Func<CancellationToken, Task> action, bool quietErrors = false)
    {
        IsBusy = true;
        Status = message;
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status = quietErrors && _accounts.Selected is null ? null : ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
