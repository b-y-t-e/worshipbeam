using Uwielbienia.Core.Presentation;

namespace Uwielbienia.Link;

/// <summary>
/// Pieśni dodane z telefonu, które telefon może jeszcze cofnąć: tylko przez <see cref="UndoTime"/> i tylko póki
/// nie trafiły na ekran (pokazana raz — nawet po zejściu z ekranu — już nie do cofnięcia).
/// </summary>
public sealed class UndoableAdditions
{
    /// <summary>Telefon pokazuje „Cofnij” przez 5 s; zapas na opóźnienie sieci.</summary>
    public static readonly TimeSpan UndoTime = TimeSpan.FromSeconds(10);

    private readonly Dictionary<Guid, (DateTime At, string? Device)> _addedAt = [];
    private readonly Func<DateTime> _now;

    public UndoableAdditions(ILiveStateSource live, Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.UtcNow);
        live.StateChanged += (_, state) => Forget(state.Item?.PlanItemId);
    }

    public void Add(Guid itemId, string? device = null) => _addedAt[itemId] = (_now(), device);

    /// <summary>Zdejmuje dodanie z listy; <c>true</c>, gdy wolno je jeszcze cofnąć — tylko urządzeniu, które je dodało.</summary>
    public bool TryTake(Guid itemId, string? device = null)
    {
        if (!_addedAt.TryGetValue(itemId, out var added) || added.Device != device)
            return false;
        _addedAt.Remove(itemId);
        return _now() - added.At <= UndoTime;
    }

    private void Forget(Guid? shownItemId)
    {
        if (shownItemId is { } id)
            _addedAt.Remove(id);
    }
}
