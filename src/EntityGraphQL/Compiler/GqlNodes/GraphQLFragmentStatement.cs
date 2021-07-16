using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace EntityGraphQL.Compiler
{
    public class GraphQLFragmentStatement : IGraphQLStatement
    {
        public string Name { get; }

        public IEnumerable<BaseGraphQLField> Fields { get; }
        /// <summary>
        /// The ParameterExpression used for the context.n This needs to be replaced by the real parameter on execution
        /// </summary>
        /// <value></value>
        public ParameterExpression SelectContext { get; }

        public GraphQLFragmentStatement(string name, IEnumerable<BaseGraphQLField> fields, ParameterExpression selectContext)
        {
            Name = name;
            Fields = fields;
            SelectContext = selectContext;
        }

        internal List<Expression> Compile<TContext>(TContext context, GraphQLValidator validator, IServiceProvider services, List<GraphQLFragmentStatement> fragments, out List<object> allArgs)
        {
            throw new NotImplementedException();
        }
    }
}