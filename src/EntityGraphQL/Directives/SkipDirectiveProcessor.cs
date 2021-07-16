using System;
using System.ComponentModel;
using EntityGraphQL.Compiler;

namespace EntityGraphQL.Directives
{
    public class SkipDirectiveProcessor : DirectiveProcessor<SkipArguments>
    {
        public override string Name { get => "skip"; }
        public override string Description { get => "Directs the executor to skip this field or fragment when the `if` argument is true."; }
        public override bool ProcessesResult { get => false; }
        public override Type GetArgumentsType()
        {
            return typeof(SkipArguments);
        }

        public override BaseGraphQLField ProcessQuery(BaseGraphQLField field, SkipArguments arguments)
        {
            if (arguments.@if)
                return null;
            return field;
        }
    }

    public class SkipArguments
    {
        [Description("Excluded when true.")]
        public bool @if { get; set; }
    }
}