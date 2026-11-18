using System;
using System.Collections.Generic;

namespace ProbablyStolen.DigitalStorage.Client.Terminal.Interaction;

/// <summary>Coordinates back dispatch among registered windows and an independently selected native target.</summary>
public sealed class WindowBackCoordinator
{
    private readonly List<Registration> _windows = new();
    private int _handledFrame = -1;
    private int _dispatchingFrame = -1;

    /// <summary>Places a mod window above paper only when its focus is newer than every active panel.</summary>
    public static bool AbovePaper(long windowStamp, long? paperStamp) =>
        !paperStamp.HasValue || windowStamp > paperStamp.Value;

    /// <summary>Registers a live window and its back action.</summary>
    public Registration Register(Func<bool> active, Func<bool> tryBack)
    {
        var registration = new Registration(this, active, tryBack);
        _windows.Add(registration);
        return registration;
    }

    /// <summary>Dispatches through eligible mod windows ahead of the independently selected native target.</summary>
    public bool Dispatch(int frame, long? nativeStamp)
    {
        if (_handledFrame == frame || _dispatchingFrame == frame) return true;
        var snapshot = _windows.ToArray();
        var candidates = new List<(Registration Registration, int Order)>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var window = snapshot[index];
            if (window.Owner == this && window.Active()) candidates.Add((window, index));
        }

        candidates.Sort((left, right) =>
        {
            var focus = right.Registration.Stamp.CompareTo(left.Registration.Stamp);
            return focus != 0 ? focus : left.Order.CompareTo(right.Order);
        });
        _dispatchingFrame = frame;
        try
        {
            foreach (var candidate in candidates)
            {
                var registration = candidate.Registration;
                if (registration.Owner != this || !registration.Active()) continue;
                if (nativeStamp.HasValue && nativeStamp.Value >= registration.Stamp) break;
                if (!registration.TryBack()) continue;
                _handledFrame = frame;
                return true;
            }
            return false;
        }
        finally
        {
            _dispatchingFrame = -1;
        }
    }

    /// <summary>A registered window whose focus stamp can be updated or released.</summary>
    public sealed class Registration : IDisposable
    {
        internal WindowBackCoordinator? Owner;
        internal readonly Func<bool> Active;
        internal readonly Func<bool> TryBack;
        internal long Stamp;

        internal Registration(WindowBackCoordinator owner, Func<bool> active, Func<bool> tryBack)
        {
            Owner = owner;
            Active = active ?? throw new ArgumentNullException(nameof(active));
            TryBack = tryBack ?? throw new ArgumentNullException(nameof(tryBack));
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
