using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace EntityGraphQL.Compiler
{
    public class GraphQLFragmentField : BaseGraphQLField
    {
        public GraphQLFragmentField(string name)
        {
            Name = name;
        }

        public override bool HasAnyServices { get => false; }
        public GraphQLFragmentStatement Fragment { get; private set; }

        public override IEnumerable<BaseGraphQLField> Expand(List<GraphQLFragmentStatement> fragments, bool withoutServiceFields)
        {
            if (Fragment == null)
            {
                var fragment = fragments.FirstOrDefault(f => f.Name == Name) ?? throw new EntityGraphQLCompilerException($"Fragment {Name} not found in query document");
                Fragment = fragment;
            }

            if (withoutServiceFields)
                return Fragment.Fields.Where(f => !f.Services.Any());
            return Fragment.Fields;
        }

        public override ExpressionResult GetNodeExpression(IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, bool withoutServiceFields = false, Expression replaceContextWith = null, bool isRoot = false)
        {
            throw new EntityGraphQLCompilerException($"Fragment should have expanded out into non fragment fields");
        }
    }
}