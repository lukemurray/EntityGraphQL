using Xunit;
using System.Linq;
using EntityGraphQL.Schema;
using EntityGraphQL.Compiler;
using EntityGraphQL.LinqQuery;

namespace EntityGraphQL.Tests.GqlCompiling
{
    /// <summary>
    /// Tests the extended (non-GraphQL - came first) LINQ style querying functionality
    /// </summary>
    public class CompilerTests
    {
        [Fact]
        public void ExpectsOpenBrace()
        {
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
	myEntity { field1 field2 }
}"));
            Assert.Equal("Error: line 2:1 no viable alternative at input 'myEntity'", ex.Message);
        }

        [Fact]
        public void ExpectsOpenBraceForEntity()
        {
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@" {
	people name lastName }
}"));
            Assert.Equal("Field name not found on type TestSchema", ex.Message);
        }

        [Fact]
        public void ExpectsCloseBraceForEntity()
        {
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@" {
	myEntity {field1 field2 }"));
            Assert.Equal("Error: line 2:26 no viable alternative at input '<EOF>'", ex.Message);
        }

        [Fact]
        public void CanParseSimpleQuery()
        {
            var objectSchemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var tree = new GraphQLCompiler(objectSchemaProvider, new DefaultMethodProvider()).Compile(@"
{
	people { id name }
}");
            Assert.Single(tree.Operations);
            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Single(result.Data);
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void CanParseSimpleQueryOptionalComma()
        {
            var objectSchemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var tree = new GraphQLCompiler(objectSchemaProvider, new DefaultMethodProvider()).Compile(@"
{
	people { id, name }
}");
            Assert.Single(tree.Operations);
            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Single(result.Data);
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void CanQueryExtendedFields()
        {
            var objectSchemaProvider = SchemaBuilder.FromObject<TestSchema>();
            objectSchemaProvider.Type<Person>().AddField("thing", p => p.Id + " - " + p.Name, "A weird field I want");
            var tree = new GraphQLCompiler(objectSchemaProvider, new DefaultMethodProvider()).Compile(@"
{
	people { id thing }
}");
            Assert.Single(tree.Operations);
            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Single(result.Data);
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("thing", person.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void CanRemoveFields()
        {
            var schema = SchemaBuilder.FromObject<TestSchema>();
            schema.Type<Person>().RemoveField(p => p.Id);
            var ex = Assert.Throws<SchemaException>(() => { var tree = new GraphQLCompiler(schema, new DefaultMethodProvider()).Compile(@"
{
	people { id }
}"); });
            Assert.Equal("Field id not found on type Person", ex.Message);
        }

        [Fact]
        public void FailsBinaryAsQuery()
        {
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people = 9 { id name }
}"));
            Assert.Equal("Error: line 3:8 no viable alternative at input '='", ex.Message);
        }

        [Fact]
        public void CanParseMultipleEntityQuery()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id name },
	users { id }
}");

            Assert.Single(tree.Operations);
            Assert.Equal(2, tree.Operations.First().QueryFields.Count());
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["people"]));
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);

            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["users"]));
            var user = Enumerable.ElementAt((dynamic)result.Data["users"], 0);
            // we only have the fields requested
            Assert.Single(user.GetType().GetFields());
            Assert.Equal("id", user.GetType().GetFields()[0].Name);
        }

        [Fact]
        public void CanParseQueryWithRelation()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id name user { field1 } }
}");
            // People.Select(p => new { Id = p.Id, Name = p.Name, User = new { Field1 = p.User.Field1 })
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["people"]));
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(3, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
            // make sure we sub-select correctly to make the requested object graph
            Assert.Equal("user", person.GetType().GetFields()[2].Name);
            var user = person.user;
            Assert.Single(user.GetType().GetFields());
            Assert.Equal("field1", user.GetType().GetFields()[0].Name);
        }

        [Fact]
        public void CanParseQueryWithRelationDeep()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people {
        id name
		user {
			field1
			nestedRelation { id name }
		}
	}
}");
            // People.Select(p => new { Id = p.Id, Name = p.Name, User = new { Field1 = p.User.Field1, NestedRelation = new { Id = p.User.NestedRelation.Id, Name = p.User.NestedRelation.Name } })
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["people"]));
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(3, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
            // make sure we sub-select correctly to make the requested object graph
            Assert.Equal("user", person.GetType().GetFields()[2].Name);
            var user = person.user;
            Assert.Equal(2, user.GetType().GetFields().Length);
            Assert.Equal("field1", user.GetType().GetFields()[0].Name);
            Assert.Equal("nestedRelation", user.GetType().GetFields()[1].Name);
            var nested = person.user.nestedRelation;
            Assert.Equal(2, nested.GetType().GetFields().Length);
            Assert.Equal("id", nested.GetType().GetFields()[0].Name);
            Assert.Equal("name", nested.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void CanParseQueryWithCollection()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id name projects { name } }
}");
            // People.Select(p => new { Id = p.Id, Name = p.Name, User = new { Field1 = p.User.Field1 })
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["people"]));
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(3, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
            // make sure we sub-select correctly to make the requested object graph
            Assert.Equal("projects", person.GetType().GetFields()[2].Name);
            var projects = person.projects;
            Assert.Equal(1, Enumerable.Count(projects));
            var project = Enumerable.ElementAt(projects, 0);
            Assert.Equal(1, project.GetType().GetFields().Length);
            Assert.Equal("name", project.GetType().GetFields()[0].Name);
        }

        [Fact]
        public void CanParseQueryWithCollectionDeep()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id
		projects {
			name
			tasks { id name }
		}
	}
}");
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal(1, Enumerable.Count((dynamic)result.Data["people"]));
            var person = Enumerable.ElementAt((dynamic)result.Data["people"], 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            // make sure we sub-select correctly to make the requested object graph
            Assert.Equal("projects", person.GetType().GetFields()[1].Name);
            var projects = person.projects;
            Assert.Equal(1, Enumerable.Count(projects));
            var project = Enumerable.ElementAt(projects, 0);
            Assert.Equal(2, project.GetType().GetFields().Length);
            Assert.Equal("name", project.GetType().GetFields()[0].Name);
            Assert.Equal("tasks", project.GetType().GetFields()[1].Name);

            var tasks = project.tasks;
            Assert.Equal(1, Enumerable.Count(tasks));
            var task = Enumerable.ElementAt(tasks, 0);
            Assert.Equal(2, task.GetType().GetFields().Length);
            Assert.Equal("id", task.GetType().GetFields()[0].Name);
            Assert.Equal("name", task.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void FailsNonExistingField()
        {
            var ex = Assert.Throws<SchemaException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id
		projects {
			name
			blahs { id name }
		}
	}
}"));
            Assert.Equal("Field blahs not found on type Project", ex.Message);
        }
        [Fact]
        public void FailsNonExistingField2()
        {
            var ex = Assert.Throws<SchemaException>(() => new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	people { id
		projects {
			name3
		}
	}
}"));
            Assert.Equal("Field name3 not found on type Project", ex.Message);
        }

        [Fact]
        public void CanExecuteRequiredParameter()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	project(id: 55) {
		name
	}
}");

            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal("Project 3", ((dynamic)result.Data["project"]).name);
        }

        [Fact]
        public void TestAlias()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
	project(id: 55) {
		n: name
	}
}");

            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal("Project 3", ((dynamic)result.Data["project"]).n);
        }

        [Fact]
        public void TestAliasDeep()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"
{
people { id
		projects {
			n: name
		}
	}
}");

            Assert.Single(tree.Operations.First().QueryFields);
            var result = tree.ExecuteQuery(new TestSchema(), null);
            Assert.Equal("Project 3", Enumerable.First(Enumerable.First((dynamic)result.Data["people"]).projects).n);
        }
    }
}