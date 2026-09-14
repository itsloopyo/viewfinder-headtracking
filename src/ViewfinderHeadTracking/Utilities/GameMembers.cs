// SPDX-License-Identifier: MIT
// Copyright (c) 2026 itsloopyo

using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace ViewfinderHeadTracking.Utilities;

/// <summary>
/// Reflection over Viewfinder's own types, which the mod never references at
/// compile time so the build stays game-independent.
///
/// Il2CppInterop exposes the game's fields as properties wrapping the native field
/// offset, so every member read here goes through a property getter. Getters are
/// resolved once by the callers that hold them, never per frame.
/// </summary>
internal static class GameMembers
{
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.Static;

    private static MethodInfo? _findObjectOfType;
    private static MethodInfo? _getComponent;

    /// <summary>A public instance property getter, or a MissingMemberException naming it.</summary>
    internal static MethodInfo RequireGetter(Type type, string name)
    {
        return type.GetProperty(name, InstanceMembers)?.GetGetMethod()
               ?? throw new MissingMemberException(type.FullName, name);
    }

    /// <summary>A public static property getter, or a MissingMemberException naming it.</summary>
    private static MethodInfo RequireStaticGetter(Type type, string name)
    {
        return type.GetProperty(name, StaticMembers)?.GetGetMethod()
               ?? throw new MissingMemberException(type.FullName, name);
    }

    /// <summary>
    /// A compiled accessor for an instance property, returning <typeparamref name="TResult"/>
    /// (enums convert to int). The per-frame reads go through these rather than
    /// <c>MethodInfo.Invoke</c>, which boxes every value and allocates on each call.
    /// </summary>
    internal static Func<object, TResult> CompileGetter<TResult>(Type type, string name)
    {
        return CompileCall<TResult>(type, RequireGetter(type, name));
    }

    /// <summary>A compiled accessor for a public parameterless instance method.</summary>
    internal static Func<object, TResult> CompileMethod<TResult>(Type type, string name)
    {
        return CompileCall<TResult>(type, RequireMethod(type, name));
    }

    /// <summary>A compiled accessor for a public static property.</summary>
    internal static Func<TResult> CompileStaticGetter<TResult>(Type type, string name)
    {
        MethodInfo getter = RequireStaticGetter(type, name);
        return Expression.Lambda<Func<TResult>>(Expression.Convert(Expression.Call(getter), typeof(TResult))).Compile();
    }

    private static Func<object, TResult> CompileCall<TResult>(Type type, MethodInfo method)
    {
        ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
        Expression call = Expression.Call(Expression.Convert(instance, type), method);
        return Expression.Lambda<Func<object, TResult>>(Expression.Convert(call, typeof(TResult)), instance).Compile();
    }

    /// <summary>A public parameterless instance method, or a MissingMethodException naming it.</summary>
    internal static MethodInfo RequireMethod(Type type, string name)
    {
        return type.GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null)
               ?? throw new MissingMethodException(type.FullName, name);
    }

    /// <summary>
    /// The first live object of a game type in the loaded scenes, typed as that type,
    /// or null. A scene search: callers rate limit it.
    /// </summary>
    internal static object? FindObjectOfType(Type type)
    {
        _findObjectOfType ??= typeof(UnityEngine.Object).GetMethods(StaticMembers)
            .First(m => m.Name == "FindObjectOfType" && m.IsGenericMethod && m.GetParameters().Length == 0);

        return LiveOrNull(_findObjectOfType.MakeGenericMethod(type).Invoke(null, null));
    }

    /// <summary>A component of a game type on a GameObject, typed as that type, or null.</summary>
    internal static object? GetComponent(GameObject gameObject, Type type)
    {
        _getComponent ??= typeof(GameObject).GetMethods(InstanceMembers)
            .First(m => m.Name == "GetComponent" && m.IsGenericMethod && m.GetParameters().Length == 0);

        return LiveOrNull(_getComponent.MakeGenericMethod(type).Invoke(gameObject, null));
    }

    /// <summary>A destroyed Unity object compares null under Unity's == while the wrapper is still there.</summary>
    private static object? LiveOrNull(object? found)
    {
        return found is UnityEngine.Object unityObject && unityObject == null ? null : found;
    }

    /// <summary>
    /// Re-wraps an interop object as another interop type around the same native
    /// pointer. Il2CppInterop types every returned wrapper as the member's DECLARED
    /// type, so a value read through a base-typed member cannot have its own type's
    /// properties reflected until it is re-wrapped.
    /// </summary>
    internal static object Rewrap(object instance, Type type)
    {
        return Activator.CreateInstance(type, ((Il2CppObjectBase)instance).Pointer)!;
    }
}
