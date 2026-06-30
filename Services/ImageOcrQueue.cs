using System.Windows.Threading;

namespace ClipboardManager;

/// <summary>图片 OCR 后台单线程队列：不阻塞剪贴板监听与 UI。</summary>
internal sealed class ImageOcrQueue
{
    private readonly ClipboardHistoryStore _store;
    private readonly Dispatcher _uiDispatcher;
    private readonly object _gate = new();
    private readonly Queue<ClipboardEntry> _pending = new();
    private readonly HashSet<long> _queuedIds = new();
    private bool _workerRunning;
    private bool _installPromptShown;
    private CancellationTokenSource? _waitInstallCts;

    public ImageOcrQueue(ClipboardHistoryStore store, Dispatcher uiDispatcher)
    {
        _store = store;
        _uiDispatcher = uiDispatcher;
    }

    public void Enqueue(ClipboardEntry entry, AppSettings? settings, Action? onEntryUpdated = null)
    {
        if (settings?.ImageOcrEnabled != true) return;
        if (entry.Type != EntryType.Image || entry.IsQuickPaste) return;
        if (entry.ImageData is not { Length: > 0 }) return;
        if (!string.IsNullOrWhiteSpace(entry.OcrText)) return;

        lock (_gate)
        {
            if (entry.PersistedId is long id && id > 0)
            {
                if (_queuedIds.Contains(id)) return;
                _queuedIds.Add(id);
            }
            _pending.Enqueue(entry);
            entry.IsOcrPending = true;
            EnsureWorker();
        }

        NotifyEntryUpdated(entry, onEntryUpdated);
    }

    public void EnqueueBackfill(IEnumerable<ClipboardEntry> entries, AppSettings? settings, Action? onEntryUpdated = null, int maxCount = 40)
    {
        if (settings?.ImageOcrEnabled != true) return;
        var list = entries
            .Where(e => e.Type == EntryType.Image && !e.IsQuickPaste && e.ImageData is { Length: > 0 }
                        && string.IsNullOrWhiteSpace(e.OcrText) && !e.IsOcrPending)
            .Take(maxCount)
            .ToList();
        foreach (var e in list)
            Enqueue(e, settings, onEntryUpdated);
    }

    private void EnsureWorker()
    {
        if (_workerRunning) return;
        _workerRunning = true;
        _ = Task.Run(WorkerLoopAsync);
    }

    private async Task WorkerLoopAsync()
    {
        try
        {
            while (true)
            {
                ClipboardEntry? entry;
                lock (_gate)
                {
                    if (_pending.Count == 0)
                    {
                        _workerRunning = false;
                        return;
                    }
                    entry = _pending.Dequeue();
                }

                if (entry == null) continue;
                await ProcessOneAsync(entry).ConfigureAwait(false);
            }
        }
        catch
        {
            lock (_gate) { _workerRunning = false; }
        }
    }

    private async Task ProcessOneAsync(ClipboardEntry entry)
    {
        try
        {
            if (!OcrLanguageInstaller.TryCreateEngine(out _))
            {
                var missing = OcrLanguageInstaller.GetFirstMissingPreferredLanguage();
                if (missing != null)
                    await TryPromptInstallAsync(missing).ConfigureAwait(false);

                if (!OcrLanguageInstaller.TryCreateEngine(out _))
                {
                    FinishEntry(entry, null);
                    return;
                }
            }

            var text = await ImageOcrService.RecognizePngAsync(entry.ImageData!).ConfigureAwait(false);
            FinishEntry(entry, text);
        }
        catch
        {
            FinishEntry(entry, null);
        }
    }

    private void FinishEntry(ClipboardEntry entry, string? text)
    {
        entry.OcrText = text;
        entry.IsOcrPending = false;
        if (entry.PersistedId is long pid && pid > 0)
            _store.TryUpdateOcrText(pid, text);

        lock (_gate)
        {
            if (entry.PersistedId is long id && id > 0)
                _queuedIds.Remove(id);
        }

        _uiDispatcher.BeginInvoke(() => entry.RaiseOcrDisplayPropertiesChanged(), DispatcherPriority.Background);
    }

    private async Task TryPromptInstallAsync(string missingLanguageTag)
    {
        bool show;
        lock (_gate)
        {
            if (_installPromptShown) return;
            _installPromptShown = true;
            show = true;
        }

        if (!show) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _uiDispatcher.InvokeAsync(() =>
        {
            try
            {
                var owner = System.Windows.Application.Current?.MainWindow;
                var result = OcrInstallPromptWindow.ShowDialog(owner, missingLanguageTag);
                tcs.TrySetResult(result == OcrInstallPromptResult.InstallViaSettings
                                 || result == OcrInstallPromptResult.InstallElevated);
            }
            catch
            {
                tcs.TrySetResult(false);
            }
        });

        var startedInstall = await tcs.Task.ConfigureAwait(false);
        if (!startedInstall) return;

        _waitInstallCts?.Cancel();
        _waitInstallCts = new CancellationTokenSource();
        try
        {
            await OcrLanguageInstaller.WaitForLanguageInstalledAsync(
                missingLanguageTag,
                TimeSpan.FromMinutes(8),
                _waitInstallCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
    }

    private static void NotifyEntryUpdated(ClipboardEntry entry, Action? onEntryUpdated)
    {
        entry.RaiseOcrDisplayPropertiesChanged();
        onEntryUpdated?.Invoke();
    }
}
