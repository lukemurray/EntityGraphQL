using System;
using System.ComponentModel;
using EntityGraphQL.Compiler;

namespace EntityGraphQL.Directives
{
    public class IncludeDirectiveProcessor : DirectiveProcessor<IncludeArguments>
    {
        public override string Name { get => "include"; }
        public override string Description { get => "Directs the executor to include this field or fragment only when the `if` argument is true."; }
        public override bool ProcessesResult { get => false; }
        public override Type GetArgumentsType()
        {
            return typeof(IncludeArguments);
        }

        public override BaseGraphQLField ProcessQuery(BaseGraphQLField field, IncludeArguments arguments)
        {
            if (arguments.@if)
                return field;
            return null;
        }
    }

    public class IncludeArguments
    {
        [Description("Included when true.")]
        public bool @if { get; set; }
    }
}