using System.Collections.Generic;
using System.Linq;

namespace EntityGraphQL.Compiler
{
    public class GraphQLQueryStatement : ExecutableGraphQLStatement
    {
        public GraphQLQueryStatement(string name, IEnumerable<BaseGraphQLField> queryFields)
        {
            Name = name;
            QueryFields = queryFields.ToList();
        }
    }
}
