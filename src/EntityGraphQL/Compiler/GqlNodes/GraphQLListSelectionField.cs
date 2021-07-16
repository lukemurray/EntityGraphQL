using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityGraphQL.Compiler.Util;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Represents a field node in the GraphQL query. That operates on a list of things.
    /// query MyQuery {
    ///     people { # GraphQLListSelectionField
    ///         id, name
    ///     }
    ///     person(id: "") { id }
    /// }
    /// </summary>
    public class GraphQLListSelectionField : BaseGraphQLQueryField
    {
        private readonly ExpressionResult fieldExpression;
        private readonly ExpressionExtractor extractor;

        /// <summary>
        /// Create a new GraphQLQueryNode. Represents both fields in the query as well as the root level fields on the Query type
        /// </summary>
        /// <param name="schemaProvider">The schema provider used to build the expressions</param>
        /// <param name="name">Name of the field. Could be the alias that the user provided</param>
        /// <param name="fieldExpression">The expression that makes the field. e.g. movie => movie.Name</param>
        /// <param name="fieldParameter">The ParameterExpression used for the field expression if required.</param>
        /// <param name="fieldSelection">Any fields that will be selected from this field e.g. (in GQL) { thisField { fieldSelection1 fieldSelection2 } }</param>
        /// <param name="selectionContext">The Expression used to build the fieldSelection expressions</param>
        public GraphQLListSelectionField(string name, ExpressionResult fieldExpression, ParameterExpression fieldParameter, IEnumerable<BaseGraphQLField> fieldSelection, ExpressionResult selectionContext)
        {
            Name = name;
            this.fieldExpression = fieldExpression;
            queryFields = fieldSelection?.ToList() ?? new List<BaseGraphQLField>();
            this.selectionContext = selectionContext;
            this.RootFieldParameter = fieldParameter;
            constantParameters = new Dictionary<ParameterExpression, object>();
            extractor = new ExpressionExtractor();
            if (fieldExpression != null)
            {
                AddServices(fieldExpression.Services);
                foreach (var item in fieldExpression.ConstantParameters)
                {
                    constantParameters.Add(item.Key, item.Value);
                }
            }
            if (fieldSelection != null)
            {
                AddServices(fieldSelection.SelectMany(s => s.GetType() == typeof(GraphQLListSelectionField) ? ((GraphQLListSelectionField)s).Services : new List<Type>()));
                foreach (var item in fieldSelection.SelectMany(fs => fs.ConstantParameters))
                {
                    constantParameters.Add(item.Key, item.Value);
                }
            }
        }

        /// <summary>
        /// The dotnet Expression for this node. Could be as simple as (Person p) => p.Name
        /// Or as complex as (DbContext ctx) => ctx.People.Where(...).Select(p => new {...}).First()
        /// If there is a object selection (new {} in a Select() or not) we will build the NodeExpression on
        /// Execute() so we can look up any query fragment selections
        /// </summary>
        public override ExpressionResult GetNodeExpression(IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, bool withoutServiceFields = false, Expression replaceContextWith = null, bool isRoot = false)
        {
            if (ShouldRebuildExpression(withoutServiceFields, replaceContextWith))
            {
                var currentContextParam = SelectionContext != null ? SelectionContext.AsParameter() : RootFieldParameter;
                var listContext = fieldExpression;
                if (replaceContextWith != null)
                {
                    var fieldType = isRoot ? replaceContextWith.Type : replaceContextWith.Type.GetField(Name)?.FieldType;
                    // we are in the second select (which contains services somewhere in the graph)
                    // Where() etc. have already been applied and the fields used may not be in the first select
                    // we need to remove them
                    // listContext
                    // if null we're in a service returned object and no longer need to replace the parameters
                    if (fieldType != null)
                    {
                        if (fieldType.IsEnumerableOrArray())
                            fieldType = fieldType.GetEnumerableOrArrayType();

                        currentContextParam = Expression.Parameter(fieldType, currentContextParam.Name);
                        // the pre services select has created the field by the Name already we just need to select that from the new context
                        listContext = isRoot ? (ExpressionResult)replaceContextWith : (ExpressionResult)Expression.PropertyOrField(replaceContextWith, Name);
                    }
                    else
                    {
                        listContext = (ExpressionResult)replacer.Replace(listContext, RootFieldParameter, replaceContextWith);
                    }
                    listContext.AddServices(fieldExpression.Services);
                }

                var selectionFields = GetSelectionFields(serviceProvider, fragments, withoutServiceFields, replaceContextWith != null ? currentContextParam : null);

                if (selectionFields == null || !selectionFields.Any())
                    return null;

                // build a .Select(...) - returning a IEnumerable<>
                var resultExpression = (ExpressionResult)ExpressionUtil.MakeSelectWithDynamicType(currentContextParam, listContext, selectionFields.ExpressionOnly());

                // only rebuild the .First/FirstOrDefault/etc if we are not later selecting service fields as the second pass will expect an array
                if (!withoutServiceFields && combineExpression != null)
                {
                    var exp = (ExpressionResult)ExpressionUtil.CombineExpressions(resultExpression, combineExpression);
                    exp.AddConstantParameters(resultExpression.ConstantParameters);
                    exp.AddServices(resultExpression.Services);
                    resultExpression = exp;
                }
                Services.AddRange(resultExpression?.Services);

                if (withoutServiceFields)
                    nodeExpressionNoServiceFields = resultExpression;
                else
                    fullNodeExpression = resultExpression;
            }

            if (fullNodeExpression != null)
            {
                fullNodeExpression.AddServices(this.Services);
                // if selecting final graph make sure lists are evaluated
                if (replaceContextWith != null && !isRoot && fullNodeExpression.Type.IsEnumerableOrArray())
                    fullNodeExpression = ExpressionUtil.MakeCallOnEnumerable("ToList", new Type[] { fullNodeExpression.Type.GetEnumerableOrArrayType() }, fullNodeExpression);
            }

            fullNodeExpression?.AddServices(this.Services);

            // above has built the expressions
            if (withoutServiceFields)
                return nodeExpressionNoServiceFields ?? fieldExpression;

            if (fullNodeExpression != null && queryFields != null && queryFields.Any())
                return fullNodeExpression;

            return fieldExpression;
        }

        protected override Dictionary<string, CompiledField> GetSelectionFields(IServiceProvider serviceProvider, List<GraphQLFragmentStatement> fragments, bool withoutServiceFields, Expression replaceContextWith)
        {
            var fields = base.GetSelectionFields(serviceProvider, fragments, withoutServiceFields, replaceContextWith);

            // extract possible fields from listContext (might be .Where(), OrderBy() etc)
            if (withoutServiceFields && fields != null)
            {
                var extractedFields = extractor.Extract(fieldExpression, SelectionContext.AsParameter(), true);
                if (extractedFields != null)
                    extractedFields.ToDictionary(i => i.Key, i =>
                    {
                        var replaced = (ExpressionResult)replacer.ReplaceByType(i.Value, SelectionContext.Type, SelectionContext);
                        return new CompiledField(new GraphQLScalarField(i.Key, replaced, RootFieldParameter, RootFieldParameter), replaced);
                    })
                    .ToList()
                    .ForEach(i =>
                    {
                        if (!fields.ContainsKey(i.Key))
                            fields.Add(i.Key, i.Value);
                    });
            }

            return fields;
        }

        public override string ToString()
        {
            return $"Node - Name={Name}, Expression={fullNodeExpression.ToString() ?? "not built yet"}";
        }
    }
}
