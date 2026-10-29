using System.Reflection;
using System.Runtime.CompilerServices;

namespace Nyra.Tests;

internal static class Reflect
{
    internal static async Task NextTickAsync()
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 == start)
            await Task.Delay(1).ConfigureAwait(false);
    }

    internal static T New<T>()
        where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    internal static void Set<TObj, TVal>(TObj target, string property, TVal value)
        where TObj : class
    {
        var declared =
            typeof(TObj) == typeof(object) && target is not null ? target.GetType() : typeof(TObj);
        var info =
            declared.GetProperty(
                property,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            ) ?? throw new MissingMemberException(declared.Name, property);
        var setter = info.GetSetMethod(true);
        if (setter is not null)
        {
            setter.Invoke(target, new object?[] { value });
            return;
        }

        Field(target, $"<{info.Name}>k__BackingField", value);
    }

    internal static void Field<TObj, TVal>(TObj target, string field, TVal value)
    {
        var start =
            typeof(TObj) == typeof(object) && target is not null ? target.GetType() : typeof(TObj);
        for (var type = start; type is not null; type = type.BaseType)
        {
            var info = type.GetField(
                field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (info is not null)
            {
                info.SetValue(target, value);
                return;
            }
        }

        throw new MissingFieldException(typeof(TObj).Name, field);
    }

    internal static object? FieldValue(object target, string field)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var info = type.GetField(
                field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (info is not null)
                return info.GetValue(target);
        }

        throw new MissingFieldException(target.GetType().Name, field);
    }

    internal static object? Invoke(object target, string method, params object?[]? arguments)
    {
        var info =
            target
                .GetType()
                .GetMethod(
                    method,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                )
            ?? throw new MissingMethodException(target.GetType().Name, method);
        return info.Invoke(target, arguments);
    }
}
