using System.Linq.Expressions;
using System.Reflection;

namespace DynamicDave.Umbraco.MapToRedirect.Sites;

internal static class InterfaceProperty
{
    /// <summary>
    /// Compiles a getter for a public interface property found by name on <typeparamref name="TTarget"/> or one of its
    /// base interfaces. Properties that moved to a base interface in a later Umbraco major (IPublishedContent.Name,
    /// Cultures and Id moved to IPublishedElement in 18) break IL compiled against the earlier one with a
    /// MissingMethodException; looking the property up at runtime works on both.
    /// </summary>
    public static Func<TTarget, TValue> Getter<TTarget, TValue>(string name)
    {
        var property = new[] { typeof(TTarget) }.Concat(typeof(TTarget).GetInterfaces())
            .Select(type => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .FirstOrDefault(candidate => candidate is { CanRead: true } && candidate.PropertyType == typeof(TValue))
            ?? throw new MissingMemberException(typeof(TTarget).FullName, name);

        var target = Expression.Parameter(typeof(TTarget), "target");
        var body = Expression.Property(Expression.Convert(target, property.DeclaringType!), property);
        return Expression.Lambda<Func<TTarget, TValue>>(body, target).Compile();
    }
}
