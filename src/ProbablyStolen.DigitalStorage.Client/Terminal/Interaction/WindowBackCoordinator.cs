using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

/// <summary>Coordinates back dispatch among registered windows and an independently selected native target.</summary>
public sealed class WindowBackCoordinator
{
    private readonly List<Registration> _windows = new();
    private int _handledFrame = -1;

    /// <summary>Places a mod window above paper only when its focus is newer than every active panel.</summary>
    public static bool AbovePaper(long windowStamp, long? paperStamp) =>
        !paperStamp.HasValue || windowStamp > paperStamp.Value;

    /// <summary>Registers a live window and its back action.</summary>
    public Registration Register(Func<bool> active, Action back)
    {
        var registration = new Registration(this, active, back);
        _windows.Add(registration);
        return registration;
    }

    /// <summary>Dispatches once per frame when the top mod window outranks the native target.</summary>
    public bool Dispatch(int frame, long? nativeStamp)
    {
        if (_handledFrame == frame) return true;
        Registration? top = null;
        foreach (var window in _windows.ToArray())
        {
            if (window.Owner == this && window.Active() && (top == null || window.Stamp > top.Stamp)) top = window;
        }

        if (top == null || (nativeStamp.HasValue && nativeStamp.Value >= top.Stamp)) return false;
        _handledFrame = frame;
        top.Back();
        return true;
    }

    /// <summary>A registered window whose focus stamp can be updated or released.</summary>
    public sealed class Registration : IDisposable
    {
        internal WindowBackCoordinator? Owner;
        internal readonly Func<bool> Active;
        internal readonly Action Back;
        internal long Stamp;

        internal Registration(WindowBackCoordinator owner, Func<bool> active, Action back)
        {
            Owner = owner;
            Active = active ?? throw new ArgumentNullException(nameof(active));
            Back = back ?? throw new ArgumentNullException(nameof(back));
        }

        /// <summary>Updates the window's shared focus stamp.</summary>
        public void Focus(long stamp) => Stamp = stamp;

        /// <summary>Unregisters the window.</summary>
        public void Dispose()
        {
            Owner?._windows.Remove(this);
            Owner = null;
        }
    }
}
