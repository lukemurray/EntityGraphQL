using Xunit;
using System.Collections.Generic;
using EntityGraphQL.Schema;

namespace EntityGraphQL.Tests
{
    public class IntrospectionTests
    {
        [Fact]
        public void TestGraphiQLIntrospection()
        {
            var schema = SchemaBuilder.FromObject<TestDataContext>();

            var gql = new QueryRequest
            {
                Query = @"
query IntrospectionQuery {
  __schema {
    queryType {
      name
    }
    mutationType {
      name
    }
    subscriptionType {
      name
    }
    types {
      ...FullType
    }
    directives {
      name
      description
      locations
      args {
        ...InputValue
      }
    }
  }
}

fragment FullType on __Type {
  kind
  name
  description
  fields(includeDeprecated: true) {
    name
    description
    args {
      ...InputValue
    }
    type {
      ...TypeRef
    }
    isDeprecated
    deprecationReason
  }
  inputFields {
    ...InputValue
  }
  interfaces {
    ...TypeRef
  }
  enumValues(includeDeprecated: true) {
    name
    description
    isDeprecated
    deprecationReason
  }
  possibleTypes {
    ...TypeRef
  }
}

fragment InputValue on __InputValue {
  name
  description
  type {
    ...TypeRef
  }
  defaultValue
}

fragment TypeRef on __Type {
  kind
  name
  ofType {
    kind
    name
    ofType {
      kind
      name
      ofType {
        kind
        name
        ofType {
          kind
          name
          ofType {
            kind
            name
            ofType {
              kind
              name
              ofType {
                kind
                name
              }
            }
          }
        }
      }
    }
  }
}
"
            };

            var context = new TestDataContext
            {
                Projects = new List<Project>()
            };

            var res = schema.ExecuteQuery(gql, context, null, null);
            Assert.Null(res.Errors);
        }

        [Fact]
        public void TestGraphiQLIntrospectionFragInFrag()
        {
            var schema = SchemaBuilder.FromObject<TestDataContext>();

            var gql = new QueryRequest
            {
                Query = @"
        query IntrospectionQuery {
          __schema {
            directives {
              args {
                ...InputValue
              }
            }
          }
        }

        fragment InputValue on __InputValue {
          type {
            ...TypeRef
          }
        }

        fragment TypeRef on __Type {
          name
        }"
            };

            var context = new TestDataContext
            {
                Projects = new List<Project>()
            };

            var res = schema.ExecuteQuery(gql, context, null, null);
            Assert.Null(res.Errors);
        }
    }
}