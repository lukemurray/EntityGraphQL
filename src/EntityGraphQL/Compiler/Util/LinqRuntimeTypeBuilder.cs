using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler.Util
{
    /// <summary>
    /// Builds .NET types at runtime and caches them to be reused
    /// </summary>
    public static class LinqRuntimeTypeBuilder
    {
        private static readonly AssemblyName assemblyName = new AssemblyName() { Name = "EntityGraphQL.DynamicTypes" };
        private static readonly ModuleBuilder moduleBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName.Name);
        private static readonly Dictionary<string, Type> builtTypes = new Dictionary<string, Type>();
        // We build a class name based on all the selected fields so we can cache the anonymous types we built
        // Names can't be > 1024 length, so we store them against Guids
        private static readonly Dictionary<string, string> typesByName = new Dictionary<string, string>();

        private static string GetTypeKey(Dictionary<string, Type> fields)
        {
            // TODO: optimize the type caching -- if fields are simply reordered, that doesn't mean that they're actually different types, so this needs to be smarter
            string key = string.Empty;
            foreach (var field in fields)
            {
                key = MakeKey(key, field.Key, field.Value);
            }

            return $"anon.{key}";
        }

        private static string MakeKey(string key, string fieldName, Type fieldType)
        {
            string type;
            if (fieldType.IsNullableType())
                type = "N" + fieldType.GetGenericArguments()[0].Name;
            else if (fieldType.IsEnumerableOrArray())
                type = "L" + fieldType.GetEnumerableOrArrayType().Name;
            else if (fieldType.GetTypeInfo().IsGenericType)
                type = $"{fieldType.Name}:{string.Join(",", fieldType.GetGenericArguments().Select(a => a.Name))}";
            else
                type = fieldType.Name;

            key += fieldName + type;
            return key;
        }

        public static Type GetDynamicType(Dictionary<string, Type> fields)
        {
            if (null == fields)
                throw new ArgumentNullException("fields");
            if (0 == fields.Count)
                throw new ArgumentOutOfRangeException("fields", "fields must have at least 1 field definition");

            try
            {
                Monitor.Enter(builtTypes);
                string className = GetTypeKey(fields);
                if (!typesByName.ContainsKey(className))
                {
                    typesByName[className] = Guid.NewGuid().ToString();
                }
                var classId = typesByName[className];

                if (builtTypes.ContainsKey(classId))
                    return builtTypes[classId];

                var typeBuilder = moduleBuilder.DefineType(classId, TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Serializable);

                foreach (var field in fields)
                {
                    var fieldBuilder = typeBuilder.DefineField(field.Key, field.Value, FieldAttributes.Public);
                }

                builtTypes[classId] = typeBuilder.CreateTypeInfo().AsType();
                return builtTypes[classId];
            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex);
            }
            finally
            {
                Monitor.Exit(builtTypes);
            }

            return null;
        }
    }
}