using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using Antlr4.Runtime;
using EntityGraphQL.Grammer;
using EntityGraphQL.LinqQuery;
using EntityGraphQL.Schema;

namespace EntityGraphQL.Compiler
{
    /// Simple language to write queries against an object schema.
    ///
    /// myEntity.where(field = 'value')
    ///
    ///   (primary_key) - e.g. myEntity(12)
    /// Binary Operators
    ///   =, !=, <, <=, >, >=, +, -, *, %, /, in
    /// Urnary Operators
    ///   not(), !
    public static class EntityQueryCompiler
    {
        public static CompiledQueryResult Compile(string query, ClaimsIdentity claims)
        {
            return Compile(query, null, claims, new DefaultMethodProvider());
        }

        public static CompiledQueryResult Compile(string query, ISchemaProvider schemaProvider, ClaimsIdentity claims)
        {
            return Compile(query, schemaProvider, claims, new DefaultMethodProvider());
        }

        /// <summary>
        /// Compile a query.
        /// </summary>
        /// <param name="query">The query text</param>
        /// <param name="schemaProvider"></param>
        /// <param name="methodProvider"></param>
        /// <returns></returns>
        public static CompiledQueryResult Compile(string query, ISchemaProvider schemaProvider, ClaimsIdentity claims, IMethodProvider methodProvider)
        {
            ParameterExpression contextParam = null;

            if (schemaProvider != null)
                contextParam = Expression.Parameter(schemaProvider.ContextType, $"cxt_{schemaProvider.ContextType.Name}");
            var expression = CompileQuery(query, contextParam, schemaProvider, claims, methodProvider);

            var contextParams = new List<ParameterExpression>();
            if (contextParam != null)
                contextParams.Add(contextParam);
            return new CompiledQueryResult(expression, contextParams);
        }

        public static CompiledQueryResult CompileWith(string query, Expression context, ISchemaProvider schemaProvider, ClaimsIdentity claims, IMethodProvider methodProvider = null)
        {
            if (methodProvider == null)
            {
                methodProvider = new DefaultMethodProvider();
            }
            var expression = CompileQuery(query, context, schemaProvider, claims, methodProvider);

            var parameters = expression.Expression.NodeType == ExpressionType.Lambda ? ((LambdaExpression)expression.Expression).Parameters.ToList() : new List<ParameterExpression>();
            return new CompiledQueryResult(expression, parameters);
        }

        private static ExpressionResult CompileQuery(string query, Expression context, ISchemaProvider schemaProvider, ClaimsIdentity claims, IMethodProvider methodProvider)
        {
            AntlrInputStream stream = new AntlrInputStream(query);
            var lexer = new EntityGraphQLLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new EntityGraphQLParser(tokens)
            {
                BuildParseTree = true
            };
            var tree = parser.eqlStart();

            var visitor = new EntityQueryNodeVisitor(context, schemaProvider, methodProvider, claims);
            var expression = visitor.Visit(tree);
            return expression;
        }
    }
}
