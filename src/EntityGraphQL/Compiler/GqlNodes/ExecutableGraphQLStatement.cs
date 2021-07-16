using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Base class for document statements that we "execute" - Query and Mutation. Execution runs the expression and gets the data result
    /// A fragment is just a definition
    /// </summary>
    public abstract class ExecutableGraphQLStatement : IGraphQLStatement
    {
        public string Name { get; protected set; }
        public IEnumerable<BaseGraphQLField> QueryFields { get; protected set; }

        public virtual Task<ConcurrentDictionary<string, object>> ExecuteAsync<TContext>(TContext context, GraphQLValidator validator, IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, Func<string, string> fieldNamer, bool executeServiceFieldsSeparately = true)
        {
            // build separate expression for all root level nodes in the op e.g. op is
            // query Op1 {
            //      people { name id }
            //      movies { released name }
            // }
            // people & movies will be the 2 fields that will be 2 separate expressions
            var result = new ConcurrentDictionary<string, object>();
            foreach (var node in QueryFields)
            {
                result[node.Name] = null;
                try
                {
                    var data = CompileAndExecuteNode(context, validator, serviceProvider, fragments, node, executeServiceFieldsSeparately);

                    result[node.Name] = data;
                }
                catch (Exception)
                {
                    throw new EntityGraphQLCompilerException($"Error executing query field {node.Name}");
                }
            }
            return Task.FromResult(result);
        }

        protected object CompileAndExecuteNode(object context, GraphQLValidator validator, IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, BaseGraphQLField node, bool executeServiceFieldsSeparately)
        {
            var replacer = new ParameterReplacer();
            // For root/top level fields we need to first select the whole graph without fields that require services
            // so that EF Core 3.1+ can run and optimise the query against the DB
            // We then select the full graph from that context

            ExpressionResult expression = null;
            var contextParam = node.RootFieldParameter;

            if (node.HasAnyServices && executeServiceFieldsSeparately)
            {
                // build this first as NodeExpression may modify ConstantParameters
                // this is without fields that require services
                expression = node.GetNodeExpression(serviceProvider, fragments, withoutServiceFields: true);
                if (expression != null)
                {
                    // execute expression now and get a result that we will then perform a full select over
                    // This part is happening via EntityFramework if you use it
                    context = ExecuteExpression(expression, context, contextParam, null, node, replacer);

                    // the full selection is now on the anonymous type returned by the selection without fields. We don't know the type until now
                    var newContextType = Expression.Parameter(context.GetType());

                    // we now know the selection type without services and need to build the full select on that type
                    // need to rebuild the full query
                    expression = node.GetNodeExpression(serviceProvider, fragments, replaceContextWith: newContextType, isRoot: true);
                    contextParam = newContextType;
                }
            }

            if (expression == null)
            {
                // just do things normally
                expression = node.GetNodeExpression(serviceProvider, fragments);
            }

            var data = ExecuteExpression(expression, context, contextParam, serviceProvider, node, replacer);
            return data;
        }

        private object ExecuteExpression(ExpressionResult expression, object context, ParameterExpression contextParam, IServiceProvider serviceProvider, BaseGraphQLField node, ParameterReplacer replacer)
        {
            var allArgs = new List<object> { context };

            var parameters = new List<ParameterExpression> { contextParam };

            // this is the full requested graph
            // inject dependencies into the fullSelection
            if (serviceProvider != null)
            {
                expression = GraphQLHelper.InjectServices(serviceProvider, node.Services, allArgs, expression, parameters, replacer);
            }

            if (node.ConstantParameters.Any())
            {
                foreach (var item in node.ConstantParameters)
                {
                    expression = (ExpressionResult)replacer.ReplaceByType(expression, item.Key.Type, item.Key);
                }
                parameters.AddRange(node.ConstantParameters.Keys);
                allArgs.AddRange(node.ConstantParameters.Values);
            }

            // evaluate everything
            if (expression.Type.IsEnumerableOrArray())
            {
                expression = ExpressionUtil.MakeCallOnEnumerable("ToList", new Type[] { expression.Type.GetEnumerableOrArrayType() }, expression);
            }

            var lambdaExpression = Expression.Lambda(expression, parameters.ToArray());
            return lambdaExpression.Compile().DynamicInvoke(allArgs.ToArray());
        }

        /// <summary>
        /// Given a Expression that is the whole graph without service fields, combine it with the full selection graph
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="replacer"></param>
        /// <returns></returns>
        protected ExpressionResult CombineNodeExpressionWithSelection(ExpressionResult expression, ExpressionResult fullNodeExpression, ParameterReplacer replacer, ParameterExpression newContextParam)
        {
            if (fullNodeExpression.NodeType != ExpressionType.Call)
                throw new Exception($"Unexpected NoteType {fullNodeExpression.NodeType}");

            var call = (MethodCallExpression)fullNodeExpression;
            if (call.Method.Name != "Select")
                throw new Exception($"Unexpected method {call.Method.Name}");

            var newExp = replacer.Replace(call.Arguments[1], QueryFields.First().RootFieldParameter, newContextParam);
            if (newExp.NodeType == ExpressionType.Quote)
                newExp = ((UnaryExpression)newExp).Operand;

            if (newExp.NodeType != ExpressionType.Lambda)
                throw new EntityGraphQLCompilerException($"Error compling query. Unexpected expression type {newExp.NodeType}");

            var selection = (LambdaExpression)newExp;
            var newCall = ExpressionUtil.MakeCallOnQueryable("Select", new Type[] { newContextParam.Type, selection.ReturnType }, expression, selection);
            return newCall;
        }
    }
}