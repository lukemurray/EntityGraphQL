using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EntityGraphQL.Compiler.Util;

namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Base class for both the ListSelectionField and ObjectProjectionField
    /// </summary>
    public abstract class BaseGraphQLQueryField : BaseGraphQLField
    {
        /// <summary>
        /// The Expression (usually a ParameterExpression or MemberExpression) used to build the Select object
        /// If the field is not IEnumerable e.g. param.Name, this is not used as the selection will be built using param.Name
        /// If the field is IEnumerable e.g. param.People, this will be a ParameterExpression of the element type of People.
        /// </summary>
        protected ExpressionResult selectionContext;
        public ExpressionResult SelectionContext => selectionContext;

        /// <summary>
        /// Holds the node's dotnet ExpressionStatement
        /// </summary>
        protected ExpressionResult fullNodeExpression;
        /// <summary>
        /// Holds the expression without any fields that use services
        /// </summary>
        protected ExpressionResult nodeExpressionNoServiceFields;

        /// <summary>
        /// Field is a complex expression (using a method or function) that returns a single object (not IEnumerable)
        /// We wrap this is a function that does a null check and avoid duplicate calls on the method/service
        /// </summary>
        /// <value></value>
        public override bool HasAnyServices { get => Services?.Any() == true || queryFields?.Any(f => f.HasAnyServices) == true; }

        protected List<BaseGraphQLField> queryFields;
        protected readonly ParameterReplacer replacer;

        protected BaseGraphQLQueryField()
        {
            if (selectionContext != null)
                Services.AddRange(selectionContext.Services);
            replacer = new ParameterReplacer();
        }

        /// <summary>
        /// The fields that this node selects
        /// query {
        ///     rootField { queryField1 queryField2 ... }
        // }
        /// </summary>
        public IEnumerable<BaseGraphQLField> QueryFields { get => queryFields; }

        public override IEnumerable<BaseGraphQLField> Expand(List<GraphQLFragmentStatement> fragments, bool withoutServiceFields) => new List<BaseGraphQLField> { this };

        protected bool ShouldRebuildExpression(bool withoutServiceFields, Expression replaceContextWith)
        {
            return (nodeExpressionNoServiceFields == null && withoutServiceFields) ||
                                        (replaceContextWith != null && fullNodeExpression != null) ||
                                        (fullNodeExpression == null && queryFields.Any());
        }
        protected virtual Dictionary<string, CompiledField> GetSelectionFields(IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, bool withoutServiceFields, Expression replaceContextWith)
        {
            // do we have services at this level
            if (withoutServiceFields && Services.Any())
                return null;

            var selectionFields = new Dictionary<string, CompiledField>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in queryFields)
            {
                if (SelectionContext != null && field is GraphQLFragmentField fragField)
                {
                    replaceContextWith ??= SelectionContext;
                }

                // Might be a fragment that expands into many fields hence the Expand
                // or a service field that we expand into the required fields for input
                foreach (var subField in field.Expand(fragments, withoutServiceFields))
                {
                    var fieldExp = subField.GetNodeExpression(serviceProvider, fragments, withoutServiceFields, replaceContextWith);

                    // pull up any services
                    AddServices(fieldExp?.Services);

                    selectionFields[subField.Name] = new CompiledField(subField, fieldExp);

                    // pull any constant values up
                    foreach (var item in subField.ConstantParameters)
                    {
                        if (!constantParameters.ContainsKey(item.Key))
                            constantParameters.Add(item.Key, item.Value);
                    }
                }
            }

            return selectionFields;
        }

        public void SetNodeExpression(ExpressionResult expr)
        {
            fullNodeExpression = expr;
        }
    }
}