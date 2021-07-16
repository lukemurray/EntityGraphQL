using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityGraphQL.Compiler;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Schema
{
    public class MutationType : IField
    {
        private readonly object mutationClassInstance;
        private readonly MethodInfo method;
        private readonly Dictionary<string, ArgType> argumentTypes = new Dictionary<string, ArgType>();
        private readonly Type argInstanceType;
        private readonly bool isAsync;

        public string Description { get; }

        public string Name { get; }
        public RequiredClaims AuthorizeClaims { get; }

        public IDictionary<string, ArgType> Arguments => argumentTypes;

        public GqlTypeInfo ReturnType { get; }

        public async Task<object> CallAsync(object context, Dictionary<string, ExpressionResult> gqlRequestArgs, GraphQLValidator validator, IServiceProvider serviceProvider, Func<string, string> fieldNamer)
        {
            // args in the mutation method
            var allArgs = new List<object>();

            object argInstance = null;
            if (gqlRequestArgs != null)
            {
                // second arg is the arguments for the mutation - required as last arg in the mutation method
                argInstance = AssignArgValues(gqlRequestArgs, fieldNamer);
                VaildateModelBinding(argInstance, validator);
                if (validator.Errors.Any())
                    return null;
            }

            // add parameters and any DI services
            foreach (var p in method.GetParameters())
            {
                if (p.GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null || p.ParameterType.GetTypeInfo().GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null)
                {
                    allArgs.Add(argInstance);
                }
                else if (p.ParameterType == context.GetType())
                {
                    allArgs.Add(context);
                }
                // todo we should put this in the IServiceCollection actually...
                else if (p.ParameterType == typeof(GraphQLValidator))
                {
                    allArgs.Add(validator);
                }
                else
                {
                    var service = serviceProvider.GetService(p.ParameterType);
                    if (service == null)
                    {
                        throw new EntityGraphQLCompilerException($"Service {p.ParameterType.Name} not found for dependency injection for mutation {method.Name}");
                    }
                    allArgs.Add(service);
                }
            }

            object result;
            if (isAsync)
            {
                result = await (dynamic)method.Invoke(mutationClassInstance, allArgs.ToArray());
            }
            else
            {
                result = method.Invoke(mutationClassInstance, allArgs.ToArray());
            }
            return result;
        }

        private object AssignArgValues(Dictionary<string, ExpressionResult> gqlRequestArgs, Func<string, string> fieldNamer)
        {
            var argInstance = Activator.CreateInstance(this.argInstanceType);
            Type argType = this.argInstanceType;
            foreach (var key in gqlRequestArgs.Keys)
            {
                var foundProp = false;
                foreach (var prop in argType.GetProperties())
                {
                    var propName = fieldNamer(prop.Name);
                    if (key == propName)
                    {
                        object value = GetValue(gqlRequestArgs, propName, prop.PropertyType);
                        prop.SetValue(argInstance, value);
                        foundProp = true;
                    }
                }
                if (!foundProp)
                {
                    foreach (var field in argType.GetFields())
                    {
                        var fieldName = fieldNamer(field.Name);
                        if (key == fieldName)
                        {
                            object value = GetValue(gqlRequestArgs, fieldName, field.FieldType);
                            field.SetValue(argInstance, value);
                            foundProp = true;
                        }
                    }
                }
                if (!foundProp)
                {
                    throw new EntityQuerySchemaException($"Could not find property or field {key} on schema object {argType.Name}");
                }
            }
            return argInstance;
        }

        /// <summary>
        /// Used at runtime below
        /// </summary>
        /// <param name="input"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private static List<T> ConvertArray<T>(Array input)
        {
            return input.Cast<T>().ToList(); // Using LINQ for simplicity
        }

        private object GetValue(Dictionary<string, ExpressionResult> gqlRequestArgs, string memberName, Type memberType)
        {
            object value = Expression.Lambda(gqlRequestArgs[memberName]).Compile().DynamicInvoke();
            if (value != null)
            {
                Type type = value.GetType();
                if (type.IsArray && memberType.IsEnumerableOrArray())
                {
                    var convertMethod = typeof(MutationType).GetMethod("ConvertArray", BindingFlags.NonPublic | BindingFlags.Static);
                    var generic = convertMethod.MakeGenericMethod(new[] { memberType.GetGenericArguments()[0] });
                    value = generic.Invoke(null, new object[] { value });
                }
                else if (type == typeof(Newtonsoft.Json.Linq.JObject))
                {
                    value = ((Newtonsoft.Json.Linq.JObject)value).ToObject(memberType);
                }
                else
                {
                    value = ExpressionUtil.ChangeType(value, memberType);
                }
            }
            return value;
        }

        public MutationType(ISchemaProvider schema, string methodName, GqlTypeInfo returnType, object mutationClassInstance, MethodInfo method, string description, RequiredClaims authorizeClaims, bool isAsync, Func<string, string> fieldNamer)
        {
            Description = description;
            ReturnType = returnType;
            this.mutationClassInstance = mutationClassInstance;
            this.method = method;
            Name = methodName;
            AuthorizeClaims = authorizeClaims;
            this.isAsync = isAsync;

            argInstanceType = method.GetParameters()
                .FirstOrDefault(p => p.GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null || p.ParameterType.GetTypeInfo().GetCustomAttribute(typeof(MutationArgumentsAttribute)) != null)?.ParameterType;
            if (argInstanceType != null)
            {
                foreach (var item in argInstanceType.GetProperties())
                {
                    if (GraphQLIgnoreAttribute.ShouldIgnoreMemberFromInput(item))
                        continue;
                    argumentTypes.Add(fieldNamer(item.Name), ArgType.FromProperty(schema, item));
                }
                foreach (var item in argInstanceType.GetFields())
                {
                    if (GraphQLIgnoreAttribute.ShouldIgnoreMemberFromInput(item))
                        continue;
                    argumentTypes.Add(fieldNamer(item.Name), ArgType.FromField(schema, item));
                }
            }
        }

        public bool HasArgumentByName(string argName)
        {
            return argumentTypes.ContainsKey(argName);
        }

        public ArgType GetArgumentType(string argName)
        {
            if (!argumentTypes.ContainsKey(argName))
            {
                throw new EntityQuerySchemaException($"Argument type not found for argument '{argName}'");
            }
            return argumentTypes[argName];
        }

        private void VaildateModelBinding(object entity, GraphQLValidator validator)
        {
            Type argType = entity.GetType();
            foreach (var prop in argType.GetProperties())
            {
                object value = prop.GetValue(entity, null);

                // set default message in-case user didn't provide a custom one
                string error = $"{prop.Name} is required";

                if (validator.Errors.Any(x => x.Message == error))
                    return;

                if (prop.GetCustomAttribute(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute)) is System.ComponentModel.DataAnnotations.RequiredAttribute attr)
                {
                    if (attr.ErrorMessage != null)
                        error = attr.ErrorMessage;

                    if (value == null)
                        validator.AddError(error);
                    else if (!attr.AllowEmptyStrings && prop.PropertyType == typeof(string) && ((string)value).Length == 0)
                        validator.AddError(error);
                }
            }
        }

    }
}