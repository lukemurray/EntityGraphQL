using Xunit;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using EntityGraphQL.Schema;
using System.Linq.Expressions;
using System;

namespace EntityGraphQL.Tests
{
    /// <summary>
    /// Tests graphql metadata
    /// </summary>
    public class MutationTests
    {
        [Fact]
        public void MissingRequiredVar()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            schemaProvider.AddInputType<InputObject>("InputObject", "Using an object in the arguments");
            schemaProvider.AddMutationFrom(new PeopleMutations());
            // Add a argument field with a require parameter
            var gql = new QueryRequest
            {
                Query = @"mutation AddPerson($name: String!) {
  addPerson(name: $name) { id name }
}",
                Variables = new QueryVariables { { "na", "Frank" } }
            };
            dynamic addPersonResult = schemaProvider.ExecuteQuery(gql, new TestSchema(), null, null).Errors;
            var err = Enumerable.First(addPersonResult);
            Assert.Equal("Missing required variable 'name' on query 'AddPerson'", err.Message);
        }

        [Fact]
        public void SupportsMutationOptional()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            schemaProvider.AddInputType<InputObject>("InputObject", "Using an object in the arguments");
            schemaProvider.AddMutationFrom(new PeopleMutations());
            // Add a argument field with a require parameter
            var gql = new QueryRequest
            {
                Query = @"
                mutation AddPerson($name: String) {
  addPerson(name: $name) {
    id name
  }
}",
                Variables = new QueryVariables { }
            };
            dynamic addPersonResult = schemaProvider.ExecuteQuery(gql, new TestSchema(), null, null).Data["addPerson"];
            // we only have the fields requested
            Assert.Equal(2, addPersonResult.GetType().GetFields().Length);
            Assert.Equal("id", addPersonResult.GetType().GetFields()[0].Name);
            Assert.Equal(555, addPersonResult.id);
            Assert.Equal("name", addPersonResult.GetType().GetFields()[1].Name);
            Assert.Equal("Default", addPersonResult.name);
        }

        [Fact]
        public void SupportsMutationArray()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            schemaProvider.AddInputType<InputObject>("InputObject", "Using an object in the arguments");
            schemaProvider.AddMutationFrom(new PeopleMutations());
            // Add a argument field with a require parameter
            var gql = new QueryRequest
            {
                Query = @"mutation AddPerson($names: [String]) {
  addPersonNames(names: $names) {
    id name lastName
  }
}",
                Variables = new QueryVariables {
                    {"names", new [] {"Bill", "Frank"}}
                }
            };
            var testSchema = new TestSchema();
            var results = schemaProvider.ExecuteQuery(gql, testSchema, null, null);
            Assert.Null(results.Errors);
            dynamic addPersonResult = results.Data["addPersonNames"];
            // we only have the fields requested
            Assert.Equal(3, addPersonResult.GetType().GetFields().Length);
            Assert.Equal("id", addPersonResult.GetType().GetFields()[0].Name);
            Assert.Equal(11, addPersonResult.id);
            Assert.Equal("name", addPersonResult.GetType().GetFields()[1].Name);
            Assert.Equal("Bill", addPersonResult.name);
            Assert.Equal("Frank", addPersonResult.lastName);
        }

        [Fact]
        public void SupportsMutationObject()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            schemaProvider.AddInputType<InputObject>("InputObject", "Using an object in the arguments");
            schemaProvider.AddMutationFrom(new PeopleMutations());
            // Add a argument field with a require parameter
            var gql = new QueryRequest
            {
                Query = @"mutation AddPerson($names: [String]) {
  addPersonInput(nameInput: $names) {
    id name lastName
  }
}",
                // object will come through as json in the request
                Variables = new QueryVariables {
                    {"names", Newtonsoft.Json.JsonConvert.DeserializeObject("{\"name\": \"Lisa\", \"lastName\": \"Simpson\"}")}
                }
            };
            var result = schemaProvider.ExecuteQuery(gql, new TestSchema(), null, null);
            Assert.Null(result.Errors);
            dynamic addPersonResult = (dynamic)result.Data["addPersonInput"];
            // we only have the fields requested
            Assert.Equal(3, addPersonResult.GetType().GetFields().Length);
            Assert.Equal("id", addPersonResult.GetType().GetFields()[0].Name);
            Assert.Equal(0, addPersonResult.id);
            Assert.Equal("name", addPersonResult.GetType().GetFields()[1].Name);
            Assert.Equal("Lisa", addPersonResult.name);
            Assert.Equal("Simpson", addPersonResult.lastName);
        }

        [Fact]
        public void SupportsSelectionFromConstant()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            schemaProvider.AddInputType<InputObject>("InputObject", "Using an object in the arguments");
            schemaProvider.AddMutationFrom(new PeopleMutations());
            // Add a argument field with a require parameter
            var gql = new QueryRequest
            {
                Query = @"mutation AddPerson($name: String) {
  addPersonAdv(name: $name) {
    id name projects { id }
  }
}",
                Variables = new QueryVariables {
                    {"name", "Bill"}
                }
            };
            var testSchema = new TestSchema();
            var results = schemaProvider.ExecuteQuery(gql, testSchema, null, null);
            dynamic addPersonResult = results.Data;
            addPersonResult = Enumerable.First(addPersonResult);
            addPersonResult = addPersonResult.Value;
            // we only have the fields requested
            Assert.Equal(3, addPersonResult.GetType().GetFields().Length);
            Assert.Equal("id", addPersonResult.GetType().GetFields()[0].Name);
            Assert.Equal("projects", addPersonResult.GetType().GetFields()[2].Name);
            Assert.Equal(1, Enumerable.Count(addPersonResult.projects));
            Assert.Equal("Bill", addPersonResult.name);
        }
    }

    internal class TestSchema
    {
        public TestSchema()
        {
            People = new List<Person> { new Person() };
        }
        public string Hello { get { return "returned value"; } }
        public List<Person> People { get; set; }
        public IEnumerable<User> Users { get { return new List<User> { new User() }; } }
    }

    internal class DbTestSchema
    {
        public string Hello { get { return "returned value"; } }
        public DbSet<Person> People { get; }
        public DbSet<User> Users { get; }
    }


    public class User
    {
        public int Id { get { return 100; } }
        public int Field1 { get { return 2; } }
        public string Field2 { get { return "2"; } }
        public Person Relation { get { return new Person(); } }
        public Task NestedRelation { get { return new Task(); } }
    }

    internal class PeopleMutations
    {
        [GraphQLMutation]

        public Person AddPerson(PeopleMutationsArgs args)
        {
            return new Person { Name = string.IsNullOrEmpty(args.Name) ? "Default" : args.Name, Id = 555 };
        }

        [GraphQLMutation]

        public Expression<Func<TestSchema, Person>> AddPersonNames(TestSchema db, PeopleMutationsArgs args)
        {
            db.People.Add(new Person { Id = 11, Name = args.Names[0], LastName = args.Names[1] });
            return ctx => ctx.People.First(p => p.Id == 11);
        }

        [GraphQLMutation]

        public Person AddPersonInput(PeopleMutationsArgs args)
        {
            return new Person { Name = args.NameInput.Name, LastName = args.NameInput.LastName };
        }

        [GraphQLMutation]
        public Expression<Func<TestSchema, Person>> AddPersonAdv(PeopleMutationsArgs args)
        {
            // test returning a constant in the expression which allows graphql selection over the schema (assuming the constant is a type in the schema)
            // Ie. in the mutation query you can select any valid fields in the schema from Person
            var person = new Person
            {
                Name = args.Name,
                Tasks = new List<Task> { new Task { Name = "A" } },
                Projects = new List<Project> { new Project { Id = 123 } }
            };
            return ctx => person;
        }
    }


    [MutationArguments]
    internal class PeopleMutationsArgs
    {
        public string Name { get; set; }
        public List<string> Names { get; set; }

        public InputObject NameInput { get; set; }
    }

    public class InputObject
    {
        public string Name { get; set; }
        public string LastName { get; set; }
    }
}