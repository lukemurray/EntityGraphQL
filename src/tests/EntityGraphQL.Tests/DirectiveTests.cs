using Xunit;
using EntityGraphQL.Schema;
using EntityGraphQL.Directives;

namespace EntityGraphQL.Tests.GqlCompiling
{
    /// <summary>
    /// Tests directives
    /// </summary>
    public class DirectiveTests
    {
        [Fact]
        public void TestIncludeIfTrueConstant()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query {
  people {
      id
      name @include(if: true)
    }
}"
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }
        [Fact]
        public void TestIncludeIfFalseConstant()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query {
  people {
      id
      name @include(if: false)
    }
}"
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            Assert.Null(result.Errors);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Single(person.GetType().GetFields());
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
        }
        [Fact]
        public void TestIncludeIfTrueVariable()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query MyQuery($include: Boolean!){
  people {
      id
      name @include(if: $include)
    }
}",
                Variables = new QueryVariables { { "include", true } }
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void TestSkipIfTrueConstant()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query {
  people {
      id
      name @skip(if: true)
    }
}"
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Single(person.GetType().GetFields());
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
        }
        [Fact]
        public void TestSkipIfFalseConstant()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query {
  people {
      id
      name @skip(if: false)
    }
}"
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }
        [Fact]
        public void TestSkipIfFalseVariable()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var query = new QueryRequest
            {
                Query = @"query MyQuery($skip: Boolean!){
  people {
      id
      name @skip(if: $skip)
    }
}",
                Variables = new QueryVariables { { "skip", true } }
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Equal(1, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
        }

        [Fact(Skip = "Not implemented yet")]
        public void TestDirectiveOnResult()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            schemaProvider.AddDirective("format", new FormatDateDirective());
            var query = new QueryRequest
            {
                Query = @"query MyQuery($skip: Boolean!){
  people {
      birthday @format(as: ""MMM"")
    }
}",
                Variables = new QueryVariables { { "skip", true } }
            };
            var result = schemaProvider.ExecuteQuery(query, new TestSchema(), null, null, null);
            dynamic person = ((dynamic)result.Data["people"])[0];
            Assert.Equal(1, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
        }
    }
}