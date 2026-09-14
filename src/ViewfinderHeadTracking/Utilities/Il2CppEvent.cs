// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Reflection;
using Il2CppInterop.Runtime;

namespace ViewfinderHeadTracking.Utilities;

/// <summary>
/// Subscribes a managed handler to a static Unity event on the IL2CPP side by
/// combining it into the event's backing delegate field.
///
/// Two things rule out the obvious routes. The NuGet reference assemblies declare
/// these as C# events of <c>System.Action</c> while the interop assemblies expose
/// <c>Il2CppSystem.Action</c>, so a compile-time <c>+=</c> binds to a member the
/// game does not have. And the <c>add_</c> accessors themselves are stripped from
/// this build wherever no game code subscribed: calling
/// <c>RenderPipelineManager.add_beginContextRendering</c> throws "Method unstripping
/// failed". The backing field survives stripping because the engine's own
/// dispatcher reads it, which is also what makes writing it take effect.
/// </summary>
internal sealed class Il2CppEvent : IDisposable
{
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static;

    private readonly PropertyInfo _field;
    private readonly Il2CppSystem.Delegate _ours;
    private bool _disposed;

    private Il2CppEvent(PropertyInfo field, Il2CppSystem.Delegate ours)
    {
        _field = field;
        _ours = ours;
    }

    internal static Il2CppEvent Subscribe(Type owner, string eventName, Delegate handler)
    {
        // Il2CppInterop surfaces a static field as a static property of the same name.
        PropertyInfo field = owner.GetProperty(eventName, StaticMembers)
            ?? throw new MissingFieldException(owner.FullName, eventName);

        MethodInfo convert = typeof(DelegateSupport).GetMethod(nameof(DelegateSupport.ConvertDelegate), StaticMembers)!
            .MakeGenericMethod(field.PropertyType);
        var ours = (Il2CppSystem.Delegate)(convert.Invoke(null, new object[] { handler })
            ?? throw new InvalidOperationException($"Could not convert a handler to {field.PropertyType.FullName}"));

        var existing = (Il2CppSystem.Delegate?)field.GetValue(null);
        field.SetValue(null, Cast(Il2CppSystem.Delegate.Combine(existing, ours), field.PropertyType));
        return new Il2CppEvent(field, ours);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var existing = (Il2CppSystem.Delegate?)_field.GetValue(null);
        Il2CppSystem.Delegate? remaining = Il2CppSystem.Delegate.Remove(existing, _ours);
        _field.SetValue(null, remaining == null ? null : Cast(remaining, _field.PropertyType));
    }

    /// <summary>
    /// Delegate.Combine hands back a wrapper typed as Il2CppSystem.Delegate, and the
    /// field's setter wants its own delegate type, so it is re-wrapped around the
    /// same native object.
    /// </summary>
    private static object Cast(Il2CppSystem.Delegate combined, Type delegateType)
    {
        return Activator.CreateInstance(delegateType, combined.Pointer)!;
    }
}
