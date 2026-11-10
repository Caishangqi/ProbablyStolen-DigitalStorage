using System;

namespace ProbablyStolen.DigitalStorage.Platform.Storage;

internal interface IEmptyPageResetPort<T> where T : class
{
    bool CanOperate { get; }
    bool CanStart { get; }
    T Current { get; }
    int Count(T pages);
    void Prepare(Action<T> retain);
    void Attach(T pages);
    bool IsLinkedEmpty(T pages, int count);
    bool HasRecordedCount(int count);
    void WriteCount(int count);
    void SelectFirst(T pages);
    void Release(T pages);
}

/// <summary>Keeps both page trees owned until replacement or rollback and cleanup have completed.</summary>
internal sealed class EmptyPageReset<T> where T : class
{
    private T? _original;
    private T? _replacement;
    private int _originalCount;
    private bool _committed;

    internal bool Pending { get; private set; }

    internal void Request() => Pending = true;

    internal bool CanAssemble(IEmptyPageResetPort<T> port) =>
        !Pending && _original == null && _replacement == null && port.CanStart && port.Count(port.Current) == 1;

    internal void Run(IEmptyPageResetPort<T> port)
    {
        Pending = true;
        if (!port.CanOperate) throw new InvalidOperationException("The housing cannot reset pages now.");

        if (_original != null)
        {
            if (_committed)
            {
                Complete(port);
                return;
            }
            RollBack(port);
        }

        if (!port.CanStart) throw new InvalidOperationException("The housing pages are not verified empty.");
        _original = port.Current;
        _originalCount = port.Count(_original);
        try
        {
            port.Prepare(pages =>
            {
                if (_replacement != null) throw new InvalidOperationException("A replacement is already retained.");
                _replacement = pages ?? throw new InvalidOperationException("A replacement is required.");
            });
            if (_replacement == null) throw new InvalidOperationException("No replacement was prepared.");
            port.Attach(_replacement);
            if (!port.IsLinkedEmpty(_replacement, 1))
                throw new InvalidOperationException("The replacement page was not linked.");
            port.WriteCount(1);
            port.SelectFirst(_replacement);
            if (!port.IsLinkedEmpty(_replacement, 1) || !port.HasRecordedCount(1))
                throw new InvalidOperationException("The replacement page is incomplete.");
            _committed = true;
            Complete(port);
        }
        catch (Exception failure)
        {
            if (!_committed)
            {
                try { RollBack(port); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("The page reset and its rollback failed.", failure, rollbackFailure);
                }
            }
            throw;
        }
    }

    private void RollBack(IEmptyPageResetPort<T> port)
    {
        var original = _original ?? throw new InvalidOperationException("The original pages are unavailable.");
        port.Attach(original);
        if (!port.IsLinkedEmpty(original, _originalCount))
            throw new InvalidOperationException("The original pages were not restored.");
        port.WriteCount(_originalCount);
        if (!port.IsLinkedEmpty(original, _originalCount) || !port.HasRecordedCount(_originalCount))
            throw new InvalidOperationException("The original page count was not restored.");
        if (_replacement != null)
        {
            port.Release(_replacement);
            _replacement = null;
        }
        _original = null;
    }

    private void Complete(IEmptyPageResetPort<T> port)
    {
        if (_replacement == null || !port.IsLinkedEmpty(_replacement, 1) || !port.HasRecordedCount(1))
            throw new InvalidOperationException("The committed replacement is no longer complete.");
        if (_original != null)
        {
            port.Release(_original);
            _original = null;
        }
        _replacement = null;
        _committed = false;
        Pending = false;
    }
}
