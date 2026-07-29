using System;
using System.Linq;
using System.Reflection;

namespace Tests.Helpers
{
    /// <summary>
    /// Minimal reflection shim for unit-testing pure private/internal logic in
    /// isolation without altering production visibility (the project's philosophy
    /// is to keep original code untouched). Methods are matched by name + arg count.
    /// </summary>
    public static class Reflect
    {
        public static T InvokeStatic<T>(Type type, string name, params object[] args)
            => (T)Find(type, name, isStatic: true, args.Length).Invoke(null, args);

        public static void InvokeStaticVoid(Type type, string name, params object[] args)
            => Find(type, name, isStatic: true, args.Length).Invoke(null, args);

        public static T InvokeInstance<T>(object instance, string name, params object[] args)
            => (T)Find(instance.GetType(), name, isStatic: false, args.Length).Invoke(instance, args);

        private static MethodInfo Find(Type type, string name, bool isStatic, int argc)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Public |
                        (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            var m = type.GetMethods(flags)
                .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == argc);
            if (m == null)
                throw new MissingMethodException($"{type.FullName}.{name} (argc {argc}) not found");
            return m;
        }
    }
}
