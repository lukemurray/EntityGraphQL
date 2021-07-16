using System;
using System.Security.Claims;
using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using EntityGraphQL.Grammer;
using EntityGraphQL.LinqQuery;
using EntityGraphQL.Schema;

namespace EntityGraphQL.Compiler
{
    public class GraphQLCompiler
    {
        private readonly ISchemaProvider _schemaProvider;
        private readonly IMethodProvider _methodProvider;
        public GraphQLCompiler(ISchemaProvider schemaProvider, IMethodProvider methodProvider)
        {
            _schemaProvider = schemaProvider;
            _methodProvider = methodProvider;
        }

        /// Parses a GraphQL-like query syntax into a tree respresenting the requested object graph. E.g.
        /// {
        ///   entity/query {
        ///     field1,
        ///     field2,
        ///     relation { field }
        ///   },
        ///   ...
        /// }
        ///
        /// The returned DataQueryNode is a root node, it's Fields are the top level data queries
        public GraphQLDocument Compile(string query, QueryVariables variables = null, ClaimsIdentity claims = null)
        {
            if (variables == null)
            {
                variables = new QueryVariables();
            }
            return Compile(new QueryRequest { Query = query, Variables = variables }, claims);
        }
        public GraphQLDocument Compile(QueryRequest request, ClaimsIdentity claims = null)
        {
            // Setup our Antlr parser
            var stream = new AntlrInputStream(request.Query);
            var lexer = new EntityGraphQLLexer(stream);
            var tokens = new CommonTokenStream(lexer);
            var parser = new EntityGraphQLParser(tokens)
            {
                BuildParseTree = true,
                ErrorHandler = new BailErrorStrategy()
            };
            try
            {
                var tree = parser.graphQL();
                var visitor = new GraphQLVisitor(_schemaProvider, _methodProvider, request.Variables, claims);
                // visit each node. it will return a linq expression for each entity requested
                var node = (GraphQLDocument)visitor.Visit(tree);
                node.Fragments = visitor.Fragments;
                return node;
            }
            catch (ParseCanceledException pce)
            {
                if (pce.InnerException != null)
                {
                    if (pce.InnerException is NoViableAltException nve)
                    {
                        throw new EntityGraphQLCompilerException($"Error: line {nve.OffendingToken.Line}:{nve.OffendingToken.Column} no viable alternative at input '{nve.OffendingToken.Text}'", pce);
                    }
                    else if (pce.InnerException is InputMismatchException ime)
                    {
                        var expecting = string.Join(", ", ime.GetExpectedTokens());
                        throw new EntityGraphQLCompilerException($"Error: line {ime.OffendingToken.Line}:{ime.OffendingToken.Column} extraneous input '{ime.OffendingToken.Text}' expecting {expecting}", pce);
                    }
                    System.Console.WriteLine(pce.InnerException.GetType());
                    throw new EntityGraphQLCompilerException(pce.InnerException.Message, pce);
                }
                throw new EntityGraphQLCompilerException(pce.Message, pce);
            }
        }
    }

    public class SchemaException : Exception
    {
        public SchemaException(string message) : base(message) { }
        public SchemaException(string message, Exception ex) : base(message, ex) { }
    }
}
