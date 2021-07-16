using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EntityGraphQL.Compiler.Util;

namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Represents a field node in the GraphQL query. That operates on a single object.
    /// query MyQuery {
    ///     people {
    ///         id, name
    ///     }
    ///     customer { # GraphQLObjectProjectionField
    ///         id
    ///     }
    /// }
    ///
    /// Builds an expression like
    /// ctx => new { Id = ctx.Customer.Id }
    /// </summary>
    public class GraphQLObjectProjectionField : BaseGraphQLQueryField
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
        public GraphQLObjectProjectionField(string name, ExpressionResult fieldExpression, ParameterExpression fieldParameter, IEnumerable<BaseGraphQLField> fieldSelection, ExpressionResult selectionContext)
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
                bool needsServiceWrap = (Services.Any() || QueryFields.Any(f => f.Services.Any())) && !withoutServiceFields;

                if (needsServiceWrap)
                {
                    // don't replace context is needsServiceWrap as the selection fields happen internally to the wrap call on the correct context
                    var selectionFields = GetSelectionFields(serviceProvider, fragments, withoutServiceFields, null);

                    if (selectionFields == null || !selectionFields.Any())
                        return null;

                    // selectionFields is set up but we need to wrap
                    // we wrap here as we have access to the values and services etc
                    var fieldParamValues = new List<object>(ConstantParameters.Values);
                    var fieldParams = new List<ParameterExpression>(ConstantParameters.Keys);

                    var updatedExpression = Services.Any() ? GraphQLHelper.InjectServices(serviceProvider, Services, fieldParamValues, fieldExpression, fieldParams, replacer) : fieldExpression;
                    // SelectionContext will be null_wrap made in visitor
                    var nullWrapParam = SelectionContext?.AsParameter() ?? Expression.Parameter(updatedExpression.Type, "nullwrap");

                    if (replaceContextWith != null)
                    {
                        updatedExpression = (ExpressionResult)replacer.Replace(updatedExpression, RootFieldParameter, replaceContextWith);
                        nullWrapParam = Expression.Parameter(updatedExpression.Type, "nullwrap");

                        foreach (var item in selectionFields)
                        {
                            if (item.Value.Expression.Services.Any())
                                item.Value.Expression = (ExpressionResult)replacer.ReplaceByType(item.Value.Expression, fieldExpression.Type, nullWrapParam);
                            else if (item.Key != "__typename") // this is a static selection of the type name
                                // pre service selection has selected the fields as the names we expect already
                                item.Value.Expression = (ExpressionResult)Expression.PropertyOrField(nullWrapParam, item.Key);
                        }
                    }

                    // we need to make sure the wrap can resolve any services in the select
                    var selectionExpressions = selectionFields.ToDictionary(f => f.Key, f => GraphQLHelper.InjectServices(serviceProvider, f.Value.Field.Services, fieldParamValues, f.Value.Expression, fieldParams, replacer));
                    // This is the var the we use in the select - the result of the service at runtime
                    var selectionParams = new List<ParameterExpression>();
                    selectionParams.AddRange(selectionFields.Values.SelectMany(f => f.Expression.ConstantParameters.Keys));
                    var selectionParamValues = new List<object>(selectionFields.Values.SelectMany(f => f.Expression.ConstantParameters.Values));
                    selectionParamValues.AddRange(fieldParamValues);
                    selectionParams.AddRange(fieldParams);

                    updatedExpression = ExpressionUtil.WrapFieldForNullCheck(updatedExpression, selectionParams.First(), selectionParams, selectionExpressions, selectionParamValues, nullWrapParam);

                    fullNodeExpression = updatedExpression;
                }
                else
                {
                    var fieldExpressionToUse = fieldExpression;
                    if (replaceContextWith != null)
                    {
                        // pre services select has created the field Name in the new context
                        fieldExpressionToUse = (ExpressionResult)Expression.PropertyOrField(replaceContextWith, Name);
                        fieldExpressionToUse.AddServices(fieldExpression.Services);
                    }

                    // don't replace context is needsServiceWrap as the selection fields happen internally to the wrap call on the coorect context
                    var selectionFields = GetSelectionFields(serviceProvider, fragments, withoutServiceFields, replaceContextWith != null ? fieldExpressionToUse.Expression : null);

                    if (selectionFields == null || !selectionFields.Any())
                        return null;

                    if (selectionFields.Any())
                    {
                        if (!withoutServiceFields)
                        { //{p_Floor.Customer.OverrideSettings.EnableDevMode}
                            // build a new {...} - returning a single object {}
                            var newExp = ExpressionUtil.CreateNewExpression(selectionFields.ExpressionOnly(), out Type anonType);
                            // make a null check from this new expression
                            newExp = Expression.Condition(Expression.MakeBinary(ExpressionType.Equal, fieldExpressionToUse, Expression.Constant(null)), Expression.Constant(null, anonType), newExp, anonType);
                            fullNodeExpression = (ExpressionResult)newExp;
                        }
                        else
                        {
                            var newExp = ExpressionUtil.CreateNewExpression(selectionFields.ExpressionOnly(), out Type anonType);
                            nodeExpressionNoServiceFields = (ExpressionResult)Expression.Condition(Expression.MakeBinary(ExpressionType.Equal, fieldExpressionToUse, Expression.Constant(null)), Expression.Constant(null, anonType), newExp, anonType);
                        }
                    }
                }

                if (combineExpression != null)
                {
                    var exp = (ExpressionResult)ExpressionUtil.CombineExpressions(fullNodeExpression, combineExpression);
                    exp.AddConstantParameters(fullNodeExpression.ConstantParameters);
                    exp.AddServices(fullNodeExpression.Services);
                    fullNodeExpression = exp;
                }
                fullNodeExpression?.AddServices(Services);
            }

            // above has built the expressions
            if (withoutServiceFields)
                return nodeExpressionNoServiceFields ?? fieldExpression;

            if (fullNodeExpression != null && queryFields != null && queryFields.Any())
                return fullNodeExpression;

            return fieldExpression;
        }

        public override IEnumerable<BaseGraphQLField> Expand(List<GraphQLFragmentStatement> fragments, bool withoutServiceFields)
        {
            // fieldExpression might be a service method call and the arguments might have fields we need to select out
            if (withoutServiceFields && fieldExpression.NodeType == ExpressionType.Call)
            {
                var fields = extractor.Extract(fieldExpression, RootFieldParameter)
                    .Select(i => new GraphQLScalarField(i.Key, (ExpressionResult)i.Value, RootFieldParameter, RootFieldParameter)).ToList();

                if (fields.Any())
                    return fields;
            }
            return base.Expand(fragments, withoutServiceFields);
        }

        public override string ToString()
        {
            return $"Node - Name={Name}, Expression={fullNodeExpression.ToString() ?? "not built yet"}";
        }
    }
}
