using System;
using System.Reflection;

namespace EntityGraphQL.Schema
{
    /// <summary>
    /// Tell the Schema Builder that when building this field, it is not nullable in the schema
    /// </summary>
    public class GraphQLNotNullAttribute : Attribute
    {
        public GraphQLNotNullAttribute()
        {
        }

        /// <summary>
        /// Check if property is marked as being not null
        /// </summary>
        /// <param name="prop"></param>
        /// <returns></returns>
        public static bool IsMemberMarkedNotNull(MemberInfo prop)
        {
            if (prop.GetCustomAttribute(typeof(GraphQLNotNullAttribute)) is GraphQLNotNullAttribute)
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Tells the schema builder that this the element type in the List/array of this field is nullable in the schema.
    /// By default a IEnumerable<T> will have the T as non-nullable in the GraphQL schema
    /// </summary>
    public class GraphQLElementTypeNullable : Attribute
    {
        public GraphQLElementTypeNullable()
        {
        }

        /// <summary>
        /// Check if property is marked as being not null
        /// </summary>
        /// <param name="prop"></param>
        /// <returns></returns>
        public static bool IsMemberElementMarkedNullable(MemberInfo prop)
        {
            if (prop.GetCustomAttribute(typeof(GraphQLElementTypeNullable)) is GraphQLElementTypeNullable)
            {
                return true;
            }
            return false;
        }
    }
}