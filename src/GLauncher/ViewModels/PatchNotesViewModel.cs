using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GLauncher.Core.Games;

namespace GLauncher.ViewModels;

/// <summary>Вкладка «Список изменений»: версии слева, текст выбранной справа. Грузится при первом открытии.</summary>
public partial class PatchNotesViewModel(PatchNotesKind kind, bool hasSnapshots = false) : ObservableObject
{
    private IReadOnlyList<PatchNoteEntry> _all = [];
    private bool _loaded;
    private CancellationTokenSource? _bodyCts;

    public bool HasSnapshots { get; } = hasSnapshots;
    public ObservableCollection<PatchNoteEntry> Entries { get; } = [];

    [ObservableProperty] private PatchNoteEntry? selected;
    [ObservableProperty] private string? body;
    [ObservableProperty] private string? status;
    [ObservableProperty] private bool showSnapshots;

    /// <summary>Загрузить список (один раз; вызывается при открытии вкладки).</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        Status = "Загрузка списка изменений…";
        try
        {
            _all = await PatchNotes.GetAsync(kind, CancellationToken.None);
            Fill();
            Status = Entries.Count == 0 ? "Список изменений пуст." : null;
        }
        catch (Exception ex)
        {
            _loaded = false;
            Status = "Не удалось загрузить список изменений: " + ex.Message;
        }
    }

    partial void OnShowSnapshotsChanged(bool value) => Fill();

    private void Fill()
    {
        Entries.Clear();
        foreach (var e in _all.Where(e => ShowSnapshots || !HasSnapshots || !e.IsSnapshot)) Entries.Add(e);
        Selected = Entries.FirstOrDefault();
    }

    async partial void OnSelectedChanged(PatchNoteEntry? value)
    {
        _bodyCts?.Cancel();
        if (value is null) { Body = null; return; }
        var cts = _bodyCts = new CancellationTokenSource();
        Body = $"<p>{value.ShortText}</p>";
        try
        {
            var html = await PatchNotes.GetBodyAsync(value, cts.Token);
            if (!cts.IsCancellationRequested) Body = html;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested) Body = $"<p>{value.ShortText}</p><p><i>Полный текст не загрузился: {ex.Message}</i></p>";
        }
    }
}
