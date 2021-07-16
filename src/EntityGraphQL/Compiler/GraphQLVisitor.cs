using System.Linq;
using System.Linq.Expressions;
using EntityGraphQL.Extensions;
using EntityGraphQL.Grammer;
using EntityGraphQL.Schema;
using System.Collections.Generic;
using EntityGraphQL.LinqQuery;
using EntityGraphQL.Compiler.Util;
using System.Security.Claims;
using System;
using System.Reflection;

namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Visits nodes of a GraphQL request to build a representation of the query against the context objects via LINQ methods.
    /// </summary>
    /// <typeparam name="IGraphQLBaseNode"></typeparam>
    internal class GraphQLVisitor : EntityGraphQLBaseVisitor<IGraphQLNode>
    {
        private readonly ConstantVisitor constantVisitor;
        private readonly ClaimsIdentity claims;
        private readonly ISchemaProvider schemaProvider;
        private readonly IMethodProvider methodProvider;
        private readonly QueryVariables variables;

        /// <summary>
        /// The context the are building the expression on. It may be a ParameterExpression or any other expression
        /// </summary>
        private ExpressionResult currentExpressionContext;
        /// <summary>
        /// Unlike currentExpressionContext this is the root ParameterExpression. e.g.
        /// currentExpressionContext = someParam.Field1
        /// currentExpressionContext will = someParam
        /// </summary>
        private ParameterExpression rootParameterContext;
        /// <summary>
        /// As we parse the request fragments are added to this
        /// </summary>
        public List<GraphQLFragmentStatement> Fragments { get; } = new List<GraphQLFragmentStatement>();

        public GraphQLVisitor(ISchemaProvider schemaProvider, IMethodProvider methodProvider, QueryVariables variables, ClaimsIdentity claims)
        {
            this.claims = claims;
            this.schemaProvider = schemaProvider;
            this.methodProvider = methodProvider;
            this.variables = variables;
            this.constantVisitor = new ConstantVisitor(schemaProvider);
        }

        public override IGraphQLNode VisitField(EntityGraphQLParser.FieldContext context)
        {
            var fieldName = context.fieldDef.GetText();
            string schemaTypeName = schemaProvider.GetSchemaTypeNameForDotnetType(currentExpressionContext.Type);
            var actualFieldName = schemaProvider.GetActualFieldName(schemaTypeName, fieldName, claims);

            var args = context.argsCall != null ? ParseGqlCall(actualFieldName, context.argsCall) : null;
            var alias = context.alias?.name.GetText();

            if (schemaProvider.HasMutation(actualFieldName))
            {
                var resultName = alias ?? actualFieldName;
                var mutationType = schemaProvider.GetMutations().First(m => m.Name == actualFieldName);
                if (context.select != null)
                {
                    var oldRootParam = rootParameterContext;
                    rootParameterContext = Expression.Parameter(mutationType.ReturnType.SchemaType.ContextType, $"mut_{actualFieldName}");
                    var expContext = (ExpressionResult)rootParameterContext;

                    var oldContext = currentExpressionContext;
                    currentExpressionContext = expContext;
                    var select = ParseFieldSelect(expContext, actualFieldName, context.select);
                    currentExpressionContext = oldContext;
                    rootParameterContext = oldRootParam;
                    return new GraphQLMutationField(resultName, mutationType, args, select, schemaProvider.SchemaFieldNamer);
                }
                else
                {
                    return new GraphQLMutationField(resultName, mutationType, args, null, schemaProvider.SchemaFieldNamer);
                }
            }
            else
            {
                if (!schemaProvider.TypeHasField(schemaTypeName, actualFieldName, args != null ? args.Select(d => d.Key) : new string[0], claims))
                    throw new EntityGraphQLCompilerException($"Field {actualFieldName} not found on type {schemaTypeName}");

                var result = schemaProvider.GetExpressionForField(currentExpressionContext, schemaTypeName, actualFieldName, args, claims);

                BaseGraphQLField fieldResult;
                var resultName = alias ?? actualFieldName;

                if (context.select != null)
                {
                    fieldResult = ParseFieldSelect(result, resultName, context.select);
                }
                else
                {
                    fieldResult = new GraphQLScalarField(resultName, result, currentExpressionContext.AsParameter() ?? rootParameterContext, currentExpressionContext);
                }

                if (context.directive != null)
                {
                    return ProcessFieldDirective(fieldResult, context.directive);
                }
                return fieldResult;
            }
        }

        private BaseGraphQLField ProcessFieldDirective(BaseGraphQLField fieldResult, EntityGraphQLParser.DirectiveCallContext directive)
        {
            var processor = schemaProvider.GetDirective(directive.name.GetText());
            var argList = directive.directiveArgs.children.Where(c => c.GetType() == typeof(EntityGraphQLParser.GqlargContext)).Cast<EntityGraphQLParser.GqlargContext>();
            var args = argList.ToDictionary(a => a.gqlfield.GetText(), a => ParseGqlarg(null, a));
            var argType = processor.GetArgumentsType();
            var argObj = Activator.CreateInstance(argType);
            foreach (var arg in args)
            {
                var prop = argType.GetProperty(arg.Key);
                prop.SetValue(argObj, Expression.Lambda(arg.Value.Expression).Compile().DynamicInvoke());
            }
            var result = processor.ProcessQueryInternal(fieldResult, argObj);
            return result;
        }

        /// <summary>
        /// Build a Select() on the context of the field type
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public BaseGraphQLQueryField ParseFieldSelect(ExpressionResult expContext, string name, EntityGraphQLParser.ObjectSelectionContext context)
        {
            try
            {
                BaseGraphQLQueryField graphQLNode = null;
                if (expContext.Type.IsEnumerableOrArray())
                {
                    graphQLNode = BuildDynamicSelectOnCollection(expContext, name, context);
                }
                else
                {
                    // Could be a list.First().Blah that we need to turn into a select, or
                    // other levels are object selection. e.g. from the top level people query I am selecting all their children { field1, etc. }
                    // Can we turn a list.First().Blah into and list.Select(i => new {i.Blah}).First()
                    var listExp = ExpressionUtil.FindIEnumerable(expContext);
                    if (listExp.Item1 != null)
                    {
                        // yes we can
                        // rebuild the ExpressionResult so we keep any ConstantParameters
                        var item1 = (ExpressionResult)listExp.Item1;
                        item1.AddConstantParameters(expContext.ConstantParameters);
                        item1.AddServices(expContext.Services);
                        graphQLNode = BuildDynamicSelectOnCollection(item1, name, context);
                        graphQLNode.SetCombineExpression(listExp.Item2);
                    }
                    else
                    {
                        graphQLNode = BuildDynamicSelectForObjectGraph(expContext, currentExpressionContext.AsParameter(), name, context);
                    }
                }
                return graphQLNode;
            }
            catch (EntityGraphQLCompilerException ex)
            {
                throw new SchemaException($"Error compiling field {name}", ex);
            }
        }

        public Dictionary<string, ExpressionResult> ParseGqlCall(string fieldName, EntityGraphQLParser.GqlCallContext context)
        {
            var argList = context.gqlarguments.children.Where(c => c.GetType() == typeof(EntityGraphQLParser.GqlargContext)).Cast<EntityGraphQLParser.GqlargContext>();
            IField methodType = schemaProvider.GetFieldOnContext(currentExpressionContext, fieldName, claims);
            var args = argList.ToDictionary(a => a.gqlfield.GetText(), a =>
            {
                var argName = a.gqlfield.GetText();
                if (!methodType.Arguments.ContainsKey(argName))
                {
                    throw new EntityGraphQLCompilerException($"No argument '{argName}' found on field '{methodType.Name}'");
                }
                var r = ParseGqlarg(methodType, a);
                return r;
            });
            return args;
        }

        public ExpressionResult ParseGqlarg(IField fieldArgumentContext, EntityGraphQLParser.GqlargContext context)
        {
            ExpressionResult gqlVarValue = null;
            if (context.gqlVar() != null)
            {
                string varKey = context.gqlVar().GetText().TrimStart('$');
                if (variables == null)
                {
                    throw new EntityGraphQLCompilerException($"Missing variable {varKey}");
                }
                object value = variables.GetValueFor(varKey);
                gqlVarValue = (ExpressionResult)Expression.Constant(value);
            }
            else
            {
                // this is an expression
                gqlVarValue = constantVisitor.Visit(context.gqlvalue);
            }

            string argName = context.gqlfield.GetText();
            if (fieldArgumentContext != null && fieldArgumentContext.HasArgumentByName(argName))
            {
                var argType = fieldArgumentContext.GetArgumentType(argName);

                if (gqlVarValue != null && gqlVarValue.Type == typeof(string) && gqlVarValue.NodeType == ExpressionType.Constant)
                {
                    string strValue = (string)((ConstantExpression)gqlVarValue).Value;
                    if (
                        (argType.Type.TypeDotnet == typeof(Guid) || argType.Type.TypeDotnet == typeof(Guid?) ||
                        argType.Type.TypeDotnet == typeof(RequiredField<Guid>) || argType.Type.TypeDotnet == typeof(RequiredField<Guid?>)) && ConstantVisitor.GuidRegex.IsMatch(strValue))
                    {
                        return (ExpressionResult)Expression.Constant(Guid.Parse(strValue));
                    }
                    if (argType.Type.TypeDotnet.IsConstructedGenericType && argType.Type.TypeDotnet.GetGenericTypeDefinition() == typeof(EntityQueryType<>))
                    {
                        string query = strValue;
                        if (query.StartsWith("\""))
                        {
                            query = query.Substring(1, context.gqlvalue.GetText().Length - 2);
                        }
                        return BuildEntityQueryExpression(fieldArgumentContext, fieldArgumentContext.Name, argName, query);
                    }

                    var argumentNonNullType = argType.Type.TypeDotnet.IsNullableType() ? Nullable.GetUnderlyingType(argType.Type.TypeDotnet) : argType.Type.TypeDotnet;
                    if (argumentNonNullType.GetTypeInfo().IsEnum)
                    {
                        var enumName = strValue;
                        var valueIndex = Enum.GetNames(argumentNonNullType).ToList().FindIndex(n => n == enumName);
                        if (valueIndex == -1)
                        {
                            throw new EntityGraphQLCompilerException($"Value {enumName} is not valid for argument {context.gqlfield.GetText()}");
                        }
                        var enumValue = Enum.GetValues(argumentNonNullType).GetValue(valueIndex);
                        return (ExpressionResult)Expression.Constant(enumValue);
                    }
                }
            }
            return gqlVarValue;
        }

        private ExpressionResult BuildEntityQueryExpression(IField fieldArgumentContext, string fieldName, string argName, string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return null;
            }
            var prop = ((Field)fieldArgumentContext).ArgumentTypesObject.GetType().GetProperties().FirstOrDefault(p => p.Name == argName && p.PropertyType.GetGenericTypeDefinition() == typeof(EntityQueryType<>));
            if (prop == null)
                throw new EntityGraphQLCompilerException($"Can not find argument {argName} of type EntityQuery on field {fieldName}");

            var eqlt = prop.GetValue(((Field)fieldArgumentContext).ArgumentTypesObject) as BaseEntityQueryType;
            var contextParam = Expression.Parameter(eqlt.QueryType, $"q_{eqlt.QueryType.Name}");
            ExpressionResult expressionResult = EntityQueryCompiler.CompileWith(query, contextParam, schemaProvider, claims, methodProvider).ExpressionResult;
            expressionResult = (ExpressionResult)Expression.Lambda(expressionResult.Expression, contextParam);
            return expressionResult;
        }

        /// Given a syntax of someCollection { fields, to, selection, from, object }
        /// it will build a select assuming 'someCollection' is an IEnumerables
        private BaseGraphQLQueryField BuildDynamicSelectOnCollection(ExpressionResult queryResult, string resultName, EntityGraphQLParser.ObjectSelectionContext context)
        {
            var elementType = queryResult.Type.GetEnumerableOrArrayType();
            var oldRootParam = rootParameterContext;
            rootParameterContext = Expression.Parameter(elementType, $"p_{elementType.Name}");

            var exp = queryResult;

            var oldContext = currentExpressionContext;
            currentExpressionContext = (ExpressionResult)rootParameterContext;
            // visit child fields. Will be more fields
            var fieldExpressions = context.children.Select(c => (BaseGraphQLField)Visit(c)).Where(n => n != null).ToList();

            var gqlNode = new GraphQLListSelectionField(resultName, exp, oldRootParam, fieldExpressions, (ExpressionResult)rootParameterContext);

            currentExpressionContext = oldContext;
            rootParameterContext = oldRootParam;

            return gqlNode;
        }

        /// <summary>
        /// Given a syntax of { fields, to, selection, from, object } with a context
        /// it will build the correct select statement
        /// </summary>
        /// <param name="name"></param>
        /// <param name="context"></param>
        /// <param name="selectContext"></param>
        /// <returns></returns>
        private BaseGraphQLQueryField BuildDynamicSelectForObjectGraph(ExpressionResult selectFromExp, ParameterExpression selectFromParam, string name, EntityGraphQLParser.ObjectSelectionContext context)
        {
            try
            {
                // visit child fields. Will be field or entityQueries again
                // These expression will be built on the element type
                var oldContext = currentExpressionContext;
                var oldRootParam = rootParameterContext;
                rootParameterContext = selectFromExp.AsParameter() ?? rootParameterContext;
                currentExpressionContext = selectFromExp;
                ParameterExpression replacementParameter = null;
                // we might be using a service i.e. ctx => WithService((T r) => r.DoSomething(ctx.Entities.Select(f => f.Id).ToList()))
                // if we can we want to avoid calling that multiple times with a expression like
                // r.DoSomething(ctx.Entities.Select(f => f.Id).ToList()) == null ? null : new {
                //      Field = r.DoSomething(ctx.Entities.Select(f => f.Id).ToList()).Blah
                // }
                // by wrapping the whole thing in a method that does the null check once.
                // This means we build the fieldExpressions on a parameter of the result type
                bool shouldWrapField = currentExpressionContext.Services.Any();
                if (shouldWrapField)
                {
                    // replace with a parameter. The expression is compiled at execution time once
                    replacementParameter = Expression.Parameter(selectFromExp.Type, "null_wrap");
                    currentExpressionContext = (ExpressionResult)replacementParameter;
                }

                var fieldExpressions = context.children.Select(c => (BaseGraphQLField)Visit(c)).Where(n => n != null).ToList();

                var graphQLNode = new GraphQLObjectProjectionField(name, selectFromExp, selectFromParam ?? rootParameterContext, fieldExpressions, currentExpressionContext);

                currentExpressionContext = oldContext;
                rootParameterContext = oldRootParam;

                return graphQLNode;
            }
            catch (EntityGraphQLCompilerException ex)
            {
                throw new SchemaException($"Failed compiling field {name}", ex);
            }
        }

        /// <summary>
        /// This is out TOP level GQL document
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override IGraphQLNode VisitGraphQL(EntityGraphQLParser.GraphQLContext context)
        {
            var gqlResult = new GraphQLDocument(schemaProvider.SchemaFieldNamer);
            foreach (var c in context.children)
            {
                var node = (ExecutableGraphQLStatement)Visit(c);
                if (node != null)
                    gqlResult.Operations.Add(node);
            }
            return gqlResult;
        }

        /// <summary>
        /// This is one of our top level nodes/statements.
        /// query MyQuery {
        ///   entityQuery { fields [, field] },
        ///   entityQuery { fields [, field] },
        ///   ...
        /// }
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override IGraphQLNode VisitDataQuery(EntityGraphQLParser.DataQueryContext context)
        {
            var operation = GetOperation(context.operationName());
            foreach (var item in operation.Arguments.Where(a => a.DefaultValue != null))
            {
                variables[item.ArgName] = Expression.Lambda(item.DefaultValue.Expression).Compile().DynamicInvoke();
            }
            rootParameterContext = Expression.Parameter(schemaProvider.ContextType, $"ctx");
            currentExpressionContext = (ExpressionResult)rootParameterContext;
            var rootFields = new List<BaseGraphQLField>();
            // Just visit each child node. All top level will be entityQueries
            foreach (var c in context.objectSelection().children)
            {
                var n = Visit(c);
                if (n != null) // white space
                {
                    // We know it'll be one of the BaseGraphQLField types
                    rootFields.Add((BaseGraphQLField)n);
                }
            }
            var query = new GraphQLQueryStatement(operation.Name, rootFields);
            return query;
        }
        /// <summary>
        /// This is one of our top level nodes/statements.
        /// mutation MyMutation {...}
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public override IGraphQLNode VisitMutationQuery(EntityGraphQLParser.MutationQueryContext context)
        {
            var operation = GetOperation(context.operationName());
            foreach (var item in operation.Arguments.Where(a => a.DefaultValue != null))
            {
                variables[item.ArgName] = Expression.Lambda(item.DefaultValue.Expression).Compile().DynamicInvoke();
            }
            rootParameterContext = Expression.Parameter(schemaProvider.ContextType, $"ctx");
            currentExpressionContext = (ExpressionResult)rootParameterContext;
            var mutationFields = new List<GraphQLMutationField>();
            foreach (var c in context.objectSelection().children)
            {
                var n = Visit(c);
                if (n != null) // white space
                {
                    // we know it will be one the BaseGraphQLField types
                    mutationFields.Add((GraphQLMutationField)n);
                }
            }
            var mutation = new GraphQLMutationStatement(operation.Name, mutationFields);
            return mutation;
        }

        public GraphQLOperation GetOperation(EntityGraphQLParser.OperationNameContext context)
        {
            if (context == null)
            {
                return new GraphQLOperation();
            }
            var visitor = new OperationVisitor(variables, schemaProvider);
            var op = visitor.Visit(context);

            return op;
        }

        public override IGraphQLNode VisitGqlFragment(EntityGraphQLParser.GqlFragmentContext context)
        {
            // top level statement in GQL doc. Defines the fragment fields.
            // Add to the fragments and return null
            var typeName = context.fragmentType.GetText();
            var oldRootParam = rootParameterContext;
            rootParameterContext = Expression.Parameter(schemaProvider.Type(typeName).ContextType, $"frag_{typeName}");
            currentExpressionContext = (ExpressionResult)rootParameterContext;
            var fields = new List<BaseGraphQLField>();
            foreach (var item in context.fields.children)
            {
                var f = Visit(item);
                if (f != null) // white space etc
                {
                    // we know they'll be one of the BaseGraphQLField types as the statements are not allowed
                    fields.Add((BaseGraphQLField)f);
                }
            }
            Fragments.Add(new GraphQLFragmentStatement(context.fragmentName.GetText(), fields, (ParameterExpression)currentExpressionContext));
            currentExpressionContext = null;
            rootParameterContext = oldRootParam;
            return null;
        }

        public override IGraphQLNode VisitFragmentSelect(EntityGraphQLParser.FragmentSelectContext context)
        {
            // later when executing we turn this field into the defined fragment (may come later in the GQL doc)
            // Just store the name to look up when needed
            var name = context.name.GetText();
            return new GraphQLFragmentField(name);
        }
    }
}
