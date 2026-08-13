using System.Linq;
using System.Reflection;

namespace SuperSocketLite.Common;

/// <summary>
/// Copies public instance properties between two objects of the same type.
/// Used to materialize a plain config model out of an arbitrary IServerConfig / IRootConfig implementation.
/// </summary>
public static class PropertyCopier
{
    /// <summary>
    /// Copies the properties of one object to another object.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source">The source.</param>
    /// <param name="target">The target.</param>
    /// <returns></returns>
    public static T CopyPropertiesTo<T>(this T source, T target)
    {
        var sourcePropertiesDict = source!.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty)
            .ToDictionary(p => p.Name);

        var targetProperties = target!.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty);

        for (int i = 0; i < targetProperties.Length; i++)
        {
            var p = targetProperties[i];

            if (!sourcePropertiesDict.TryGetValue(p.Name, out PropertyInfo? sourceProperty))
                continue;

            if (sourceProperty.PropertyType != p.PropertyType)
                continue;

            p.SetValue(target, sourceProperty.GetValue(source, null), null);
        }

        return target;
    }
}
