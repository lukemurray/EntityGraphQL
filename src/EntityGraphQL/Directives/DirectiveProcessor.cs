using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EntityGraphQL.Compiler;
using EntityGraphQL.Schema;

namespace EntityGraphQL.Directives
{
    public interface IDirectiveProcessor
    {
        string Name { get; }
        string Description { get; }
        /// <summary>
        /// Return the Type used for the directive arguments
        /// </summary>
        /// <returns></returns>
        Type GetArgumentsType();
        /// <summary>
        /// Return true if the directive requires to make changes to the result
        /// </summary>
        /// <value></value>
        bool ProcessesResult { get; }
        BaseGraphQLField ProcessQueryInternal(BaseGraphQLField fieldResult, object arguments);
        IEnumerable<ArgType> GetArguments(ISchemaProvider schema);
    }

    /// <summary>
    /// Base directive processor. To implement custom directives inherit from this class and override either or both
    /// ProcessQuery() - used to make changes to the query before execution (e.g. @include/skip)
    /// ProcessResult() - used to make changes to the result of the item the directive is on
    /// </summary>
    public abstract class DirectiveProcessor<TArguments> : IDirectiveProcessor
    {
        public abstract Type GetArgumentsType();
        public abstract bool ProcessesResult { get; }
        public abstract string Name { get; }
        public abstract string Description { get; }

        /// <summary>
        /// Implement this to make changes to the item marked with the directive.
        /// GraphQLQueryNode details the expressions for the field query
        /// </summary>
        /// <param name="field"></param>
        /// <param name="arguments">Any arguments passed to the directive</param>
        /// <returns>Return a modified (from field) or new IGraphQLBaseNode. Returning null will remove the item from the resulting query</returns>
        public virtual BaseGraphQLField ProcessQuery(BaseGraphQLField field, TArguments arguments)
        {
            // by default we do nothing
            return field;
        }

        /// <summary>
        /// Implement this to make changes to the result
        /// </summary>
        /// <param name="value"></param>
        /// <param name="arguments"></param>
        /// <returns></returns>
        public virtual object ProcessResult(object value, TArguments arguments)
        {
            // default return the value
            return value;
        }

        public BaseGraphQLField ProcessQueryInternal(BaseGraphQLField field, object arguments)
        {
            var result = ProcessQuery(field, (TArguments)arguments);
            if (ProcessesResult)
            {
                // wrap expression in a call to process the result
                // field.Wrap(exp => Expression.Call(Expression.Constant(this), "ProcessResult", null, exp, arguments));
            }
            return result;
        }

        public IEnumerable<ArgType> GetArguments(ISchemaProvider schema)
        {
            return GetArgumentsType().GetProperties().ToList().Select(prop => ArgType.FromProperty(schema, prop)).ToList();
        }
    }
}