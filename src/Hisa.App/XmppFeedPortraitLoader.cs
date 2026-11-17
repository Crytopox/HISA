using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Hisa.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Hisa.App;

internal sealed class XmppFeedPortraitLoader(IEveCharacterResolver resolver, ILogger<XmppFeedPortraitLoader> logger,
    TimeSpan? retryDelay = null)
{
    private readonly Dictionary<string, Bitmap> _portraits = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource?> _loads = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<XmppFeedCommanderVm> _commanders = [];
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromMinutes(1);

    public void Update(IReadOnlyList<XmppFeedCardVm> cards)
    {
        Dispatcher.UIThread.VerifyAccess();
        _commanders = cards.SelectMany(card => card.HasLineup ? card.Lineup : [card.Commander])
            .Where(commander => commander.LookupName is not null).ToArray();
        var names = _commanders.Select(commander => commander.LookupName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var removed in _loads.Keys.Where(name => !names.Contains(name)).ToArray())
        {
            _loads.Remove(removed, out var load);
            load?.Cancel();
        }

        foreach (var commander in _commanders)
        {
            if (_portraits.TryGetValue(commander.LookupName!, out var portrait))
                commander.Portrait = portrait;
        }

        foreach (var name in names)
        {
            if (_portraits.ContainsKey(name) || _loads.ContainsKey(name)) continue;
            var load = new CancellationTokenSource();
            _loads.Add(name, load);
            _ = LoadAsync(name, load);
        }
    }

    private async Task LoadAsync(string name, CancellationTokenSource load)
    {
        var token = load.Token;
        long? characterId = null;
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                if (attempt > 0)
                    await WaitToRetryAsync(characterId, token);

                var stage = "character name lookup";
                try
                {
                    characterId ??= await resolver.ResolveIdByNameAsync(name, token);
                    token.ThrowIfCancellationRequested();
                    if (characterId is null)
                    {
                        logger.LogWarning("Fleet commander portrait unavailable for {CharacterName}: {Stage} returned no ID",
                            name, stage);
                        continue;
                    }

                    stage = "image download";
                    var bytes = await resolver.GetPortraitAsync("tranquility", characterId.Value, token);
                    token.ThrowIfCancellationRequested();
                    if (bytes is not { Length: > 0 })
                    {
                        logger.LogWarning("Fleet commander portrait unavailable for {CharacterName}: {Stage} returned no image",
                            name, stage);
                        continue;
                    }

                    stage = "bitmap decoding";
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = new MemoryStream(bytes, writable: false);
                        var bitmap = new Bitmap(stream);
                        _portraits[name] = bitmap;
                        foreach (var commander in _commanders)
                        {
                            if (string.Equals(commander.LookupName, name, StringComparison.OrdinalIgnoreCase))
                                commander.Portrait = bitmap;
                        }
                    });
                    return;
                }
                catch (Exception ex)
                {
                    token.ThrowIfCancellationRequested();
                    logger.LogWarning(ex, "Fleet commander portrait failed for {CharacterName} during {Stage}", name, stage);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_loads.TryGetValue(name, out var current) && ReferenceEquals(current, load))
                    _loads[name] = null;
                load.Dispose();
            });
        }
    }

    private async Task WaitToRetryAsync(long? characterId, CancellationToken token)
    {
        await Task.Delay(_retryDelay, token);
        while (true)
        {
            var retryAt = characterId.HasValue ? resolver.PortraitRetryNotBefore : resolver.NameLookupRetryNotBefore;
            var delay = retryAt - DateTimeOffset.UtcNow;
            if (delay <= TimeSpan.Zero) return;
            await Task.Delay(delay > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : delay, token);
        }
    }
}
