using System.Reflection;
using Vintagestory.API.Client;

/// <summary> Interface fake whose calls go to a handler. Same approach as the Inventory Particles and SmoothSwapping tests. </summary>
public class FakeProxy : DispatchProxy
{
    private System.Func<MethodInfo, object?[]?, object?> handler = null!;
    private static bool apiAccessConfigured;

    public static object Create(Type interfaceType, System.Func<MethodInfo, object?[]?, object?> handler)
    {
        object proxy = Create(interfaceType, typeof(FakeProxy));

        if (!apiAccessConfigured)
        {
            // IClientPlayer includes an internal default interface method. DispatchProxy normally adds this attribute
            // for internal types, but misses internal methods of public types. Give only this in-memory test proxy
            // assembly access to the installed API internals.
            Type generator = typeof(DispatchProxy).Assembly.GetType("System.Reflection.DispatchProxyGenerator", true)!;
            object map = generator.GetField("s_alcProxyAssemblyMap", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object?[] lookup = { System.Runtime.Loader.AssemblyLoadContext.Default, null };
            bool found = (bool)map.GetType().GetMethod("TryGetValue")!.Invoke(map, lookup)!;

            if (!found)
            {
                throw new Exception("DispatchProxy did not register its dynamic assembly.");
            }

            lookup[1]!.GetType().GetMethod("GenerateInstanceOfIgnoresAccessChecksToAttribute", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(lookup[1], new object[] { typeof(ICoreClientAPI).Assembly.GetName().Name! });
            apiAccessConfigured = true;
        }

        ((FakeProxy)proxy).handler = handler;
        return proxy;
    }

    public static object? Default(Type type) => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
}
