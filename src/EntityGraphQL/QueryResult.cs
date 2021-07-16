using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Newtonsoft.Json;

namespace EntityGraphQL
{
    public class QueryResult
    {
        private List<GraphQLError> errors = null;
        [JsonProperty("errors")]
        public ReadOnlyCollection<GraphQLError> Errors => errors?.AsReadOnly();
        [JsonProperty("data")]
        public ConcurrentDictionary<string, object> Data = new ConcurrentDictionary<string, object>();

        public QueryResult() { }
        public QueryResult(GraphQLError error)
        {
            errors = new List<GraphQLError> { error };
        }
        public QueryResult(IEnumerable<GraphQLError> errors)
        {
            this.errors = errors.ToList();
        }

        public void AddError(GraphQLError error)
        {
            if (errors == null)
            {
                errors = new List<GraphQLError>();
            }

            errors.Add(error);
        }

        public void AddErrors(IEnumerable<GraphQLError> errors)
        {
            if (errors == null)
            {
                errors = new List<GraphQLError>();
            }

            this.errors.AddRange(errors);
        }
    }
}