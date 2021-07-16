using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler
{
    public class GraphQLMutationStatement : ExecutableGraphQLStatement
    {
        public GraphQLMutationStatement(string name, IEnumerable<GraphQLMutationField> mutationFields)
        {
            Name = name;
            QueryFields = mutationFields.ToList();
        }

        public override async Task<ConcurrentDictionary<string, object>> ExecuteAsync<TContext>(TContext context, GraphQLValidator validator, IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, Func<string, string> fieldNamer, bool executeServiceFieldsSeparately)
        {
            var result = new ConcurrentDictionary<string, object>();
            foreach (GraphQLMutationField node in QueryFields)
            {
                result[node.Name] = null;
                try
                {
                    var data = await ExecuteAsync(node, context, validator, serviceProvider, fragments, fieldNamer, executeServiceFieldsSeparately);

                    result[node.Name] = data;
                }
                catch (Exception)
                {
                    throw new EntityGraphQLCompilerException($"Error executing query field {node.Name}");
                }
            }
            return result;
        }

        /// <summary>
        /// Execute the current mutation
        /// </summary>
        /// <param name="context">The context instance that will be used</param>
        /// <param name="serviceProvider">A service provider to look up any dependencies</param>
        /// <typeparam name="TContext"></typeparam>
        /// <returns></returns>
        private async Task<object> ExecuteAsync<TContext>(GraphQLMutationField node, TContext context, GraphQLValidator validator, IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, Func<string, string> fieldNamer, bool executeServiceFieldsSeparately)
        {
            // run the mutation to get the context for the query select
            var result = await node.ExecuteMutationAsync(context, validator, serviceProvider, fieldNamer);

            if (result == null || // result is null and don't need to do anything more
                node.ResultSelection == null) // mutation must return a scalar type
                return result;

            if (typeof(LambdaExpression).IsAssignableFrom(result.GetType()))
            {
                var mutationLambda = (LambdaExpression)result;
                var mutationContextParam = mutationLambda.Parameters.First();
                var mutationExpression = mutationLambda.Body;

                // this will typically be similar to
                // db => db.Entity.Where(filter) or db => db.Entity.First(filter)
                // i.e. they'll be returning a list of items or a specific item
                // We want to take the field selection from the GraphQL query and add a LINQ Select() onto the expression
                // In the case of a First() we need to insert that select before the first
                // This is all to have 1 nice expression that can work with ORMs (like EF)
                // E.g  we want db => db.Entity.Select(e => new {name = e.Name, ...}).First(filter)
                // we dot not want db => new {name = db.Entity.First(filter).Name, ...})

                var selectParam = node.ResultSelection.RootFieldParameter;

                if (!mutationLambda.ReturnType.IsEnumerableOrArray())
                {
                    if (mutationExpression.NodeType == ExpressionType.Call)
                    {
                        var call = (MethodCallExpression)mutationExpression;
                        if (call.Method.Name == "First" || call.Method.Name == "FirstOrDefault" ||
                            call.Method.Name == "Last" || call.Method.Name == "LastOrDefault" ||
                            call.Method.Name == "Single" || call.Method.Name == "SingleOrDefault")
                        {
                            var baseExp = call.Arguments.First();
                            if (call.Arguments.Count == 2)
                            {
                                // this is a ctx.Something.First(f => ...)
                                // move the filter to a Where call
                                var filter = call.Arguments.ElementAt(1);
                                baseExp = ExpressionUtil.MakeCallOnQueryable("Where", new Type[] { selectParam.Type }, baseExp, filter);
                            }

                            // build select
                            var selectExp = ExpressionUtil.MakeCallOnQueryable("Select", new Type[] { selectParam.Type, node.ResultSelection.GetNodeExpression(serviceProvider, fragments).Type }, baseExp, Expression.Lambda(node.ResultSelection.GetNodeExpression(serviceProvider, fragments), selectParam));

                            // add First/Last back
                            var firstExp = ExpressionUtil.MakeCallOnQueryable(call.Method.Name, new Type[] { selectExp.Type.GetGenericArguments()[0] }, selectExp);

                            // we're done
                            node.ResultSelection.SetNodeExpression(firstExp);
                        }
                    }
                    else
                    {
                        // if they just return a constant I.e the entity they just updated. It comes as a member access constant
                        if (mutationLambda.Body.NodeType == ExpressionType.MemberAccess)
                        {
                            var me = (MemberExpression)mutationLambda.Body;
                            if (me.Expression.NodeType == ExpressionType.Constant)
                            {
                                node.ResultSelection.ConstantParameters[Expression.Parameter(me.Type, $"const_{me.Type.Name}")] = Expression.Lambda(me).Compile().DynamicInvoke();
                            }
                        }
                        else if (mutationLambda.Body.NodeType == ExpressionType.Constant)
                        {
                            var ce = (ConstantExpression)mutationLambda.Body;
                            node.ResultSelection.ConstantParameters[Expression.Parameter(ce.Type, $"const_{ce.Type.Name}")] = ce.Value;
                        }
                    }
                }
                else
                {
                    var exp = ExpressionUtil.MakeCallOnQueryable("Select", new Type[] { selectParam.Type, node.ResultSelection.GetNodeExpression(serviceProvider, fragments).Type }, mutationExpression, Expression.Lambda(node.ResultSelection.GetNodeExpression(serviceProvider, fragments), selectParam));
                    node.ResultSelection.SetNodeExpression(exp);
                }

                // make sure we use the right parameter
                node.ResultSelection.RootFieldParameter = mutationContextParam;
                result = CompileAndExecuteNode(context, validator, serviceProvider, fragments, node.ResultSelection, executeServiceFieldsSeparately);
                return result;
            }

            // run the query select
            result = CompileAndExecuteNode(result, validator, serviceProvider, fragments, node.ResultSelection, executeServiceFieldsSeparately);
            return result;
        }
    }
}
