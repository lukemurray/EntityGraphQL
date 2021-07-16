using Xunit;
using System.Collections.Generic;
using System.Linq;
using static EntityGraphQL.Schema.ArgumentHelper;
using EntityGraphQL.Extensions;
using EntityGraphQL.Schema;
using EntityGraphQL.Compiler;
using System;
using EntityGraphQL.LinqQuery;

namespace EntityGraphQL.Tests.GqlCompiling
{
    public class GraphQLSyntaxTests
    {
        [Fact]
        public void SupportsQueryKeyword()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"query {
	people { id }
}");
            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["people"];
            Assert.Equal(1, Enumerable.Count(result));
            var person = Enumerable.ElementAt(result, 0);
            // we only have the fields requested
            Assert.Equal(1, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
        }

        [Fact]
        public void SupportsOptionalCommaInFieldList()
        {
            var tree = new GraphQLCompiler(SchemaBuilder.FromObject<TestSchema>(), new DefaultMethodProvider()).Compile(@"query {
	people { id, name }
}");
            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["people"];
            Assert.Equal(1, Enumerable.Count(result));
            var person = Enumerable.ElementAt(result, 0);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
        }

        [Fact]
        public void SupportsArguments()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            // Add a argument field with a require parameter
            schemaProvider.AddField("user", new { id = Required<int>() }, (ctx, param) => ctx.Users.Where(u => u.Id == param.id).FirstOrDefault(), "Return a user by ID");
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
	user(id: 1) { id }
}");
            // db => db.Users.Where(u => u.Id == id).Select(u => new {id = u.Id}]).FirstOrDefault()
            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["user"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(1, result.id);
        }

        [Fact]
        public void SupportsManyArguments()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            // Add a argument field with a require parameter
            schemaProvider.AddField("user", new { id = Required<int>(), something = true }, (ctx, param) => ctx.Users.Where(u => u.Id == param.id).FirstOrDefault(), "Return a user by ID");
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
	user(id: 1, something: false) { id }
}");
            // db => db.Users.Where(u => u.Id == id).Select(u => new {id = u.Id}]).FirstOrDefault()
            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["user"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(1, result.id);
        }

        [Fact]
        public void SupportsManyArgumentsOptionalComma()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            // Add a argument field with a require parameter
            schemaProvider.AddField("user", new { id = Required<int>(), something = true }, (ctx, param) => ctx.Users.Where(u => u.Id == param.id).FirstOrDefault(), "Return a user by ID");
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
	user(id: 1 something: false) { id }
}");
            // db => db.Users.Where(u => u.Id == id).Select(u => new {id = u.Id}]).FirstOrDefault()
            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["user"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(1, result.id);
        }

        [Fact]
        public void DoesNotSupportSameFieldDifferentArguments()
        {
            // Grpahql doesn't support "field overloading"
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(true);
            // user(id: ID) already created
            var ex = Assert.Throws<EntityQuerySchemaException>(() => schemaProvider.AddField("user", new { monkey = Required<int>() }, (ctx, param) => ctx.Users.Where(u => u.Id == param.monkey).FirstOrDefault(), "Return a user by ID"));
            Assert.Equal("Field user already exists on type TestSchema. Use ReplaceField() if this is intended.", ex.Message);
        }

        [Fact]
        public void ThrowsOnMissingRequiredArgument()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            // Add a argument field with a require parameter
            schemaProvider.AddField("user", new { id = Required<int>() }, (ctx, param) => ctx.Users.FirstOrDefault(u => u.Id == param.id), "Return a user by ID");
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                user { id }
            }"));
            Assert.Equal("Field 'user' missing required argument 'id'", ex.Message);
        }

        [Fact]
        public void ThrowsOnMissingRequiredArguments()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>(false);
            // Add a argument field with a require parameter
            schemaProvider.AddField("user", new { id = Required<int>(), h = Required<string>() }, (ctx, param) => ctx.Users.FirstOrDefault(u => u.Id == param.id), "Return a user by ID");
            var ex = Assert.Throws<EntityGraphQLCompilerException>(() => new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                user { id }
            }"));
            Assert.Equal("Field 'user' missing required argument 'id'", ex.Message);
        }

        [Fact]
        public void SupportsArgumentsDefaultValue()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a default parameter
            schemaProvider.AddField("me", new { id = 9 }, (ctx, param) => ctx.Users.Where(u => u.Id == param.id).FirstOrDefault(), "Return me, or someone else");
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                me { id }
            }");

            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["me"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(9, result.id);
        }

        [Fact]
        public void SupportsDefaultArgumentsInNonRoot()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            schemaProvider.AddEnum("HeightUnit", typeof(HeightUnit), "Unit of height measurement");
            schemaProvider.Type<Person>().ReplaceField("height", new { unit = HeightUnit.Cm }, (p, param) => p.GetHeight(param.unit), "Return me, or someone else");
            var result = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                people { id height }
            }").ExecuteQuery(new TestSchema(), null);

            Assert.Equal(1, Enumerable.Count(result.Data));
            var person = Enumerable.First((dynamic)result.Data["people"]);
            // we only have the fields requested
            Assert.Equal(2, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("height", person.GetType().GetFields()[1].Name);
            Assert.Equal(183.0, person.height);
        }

        [Fact]
        public void SupportsArgumentsInNonRoot()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            schemaProvider.AddEnum("HeightUnit", typeof(HeightUnit), "Unit of height measurement");
            schemaProvider.Type<Person>().ReplaceField("height", new { unit = HeightUnit.Cm }, (p, param) => p.GetHeight(param.unit), "Return me, or someone else");
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                people { height(unit: ""Meter"") }
            }").ExecuteQuery(new TestSchema(), null);

            dynamic result = tree.Data["people"];
            Assert.Equal(1, Enumerable.Count(result));
            var person = Enumerable.First(result);
            // we only have the fields requested
            Assert.Equal(1.83, person.height);
        }

        [Fact]
        public void SupportsArgumentsAuto()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                user(id: 1) { id }
            }");

            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["user"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(1, result.id);
        }

        [Fact]
        public void SupportsArgumentsAutoWithGuid()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                project(id: ""aaaaaaaa-bbbb-4444-1111-ccddeeff0022"") { id }
            }");

            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["project"];
            // we only have the fields requested
            Assert.Equal(1, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(new Guid("aaaaaaaa-bbbb-4444-1111-ccddeeff0022"), result.id);
        }

        [Fact]
        public void SupportsArgumentsComplex()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                person(id: ""cccccccc-bbbb-4444-1111-ccddeeff0033"") { id projects { id name } }
            }").ExecuteQuery(new TestSchema(), null);

            dynamic user = tree.Data["person"];
            // we only have the fields requested
            Assert.Equal(2, user.GetType().GetFields().Length);
            Assert.Equal("id", user.GetType().GetFields()[0].Name);
            Assert.Equal(new Guid("cccccccc-bbbb-4444-1111-ccddeeff0033"), user.id);
        }

        [Fact]
        public void SupportsArgumentsComplexInGraph()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            schemaProvider.Type<Person>().AddField("project", new { pid = Required<Guid>() }, (p, args) => p.Projects.FirstOrDefault(s => s.Id == args.pid), "Return a specific project");
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"query {
                person(id: ""cccccccc-bbbb-4444-1111-ccddeeff0033"") { id project(pid: ""aaaaaaaa-bbbb-4444-1111-ccddeeff0022"") { id name } }
            }").ExecuteQuery(new TestSchema(), null);

            dynamic user = tree.Data["person"];
            // we only have the fields requested
            Assert.Equal(2, user.GetType().GetFields().Length);
            Assert.Equal("id", user.GetType().GetFields()[0].Name);
            Assert.Equal(new Guid("cccccccc-bbbb-4444-1111-ccddeeff0033"), user.id);
            Assert.Equal(new Guid("aaaaaaaa-bbbb-4444-1111-ccddeeff0022"), user.project.id);
        }

        [Fact]
        public void SupportsComments()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
            # hey there
            query {
                # yep
                person(id: ""cccccccc-bbbb-4444-1111-ccddeeff0033"") { # this is a good field
                    id
                    # more comments
                    projects { id name }
                    # down, why not
                }
                # yo
            } # look at me
# no thanks!");

            dynamic result = tree.ExecuteQuery(new TestSchema(), null).Data["person"];

            // we only have the fields requested
            Assert.Equal(2, result.GetType().GetFields().Length);
            Assert.Equal("id", result.GetType().GetFields()[0].Name);
            Assert.Equal(new Guid("cccccccc-bbbb-4444-1111-ccddeeff0033"), result.id);
        }

        [Fact]
        public void SupportsFragmentSyntax()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
query {
    people { projects { id name } }
}
fragment info on Person {
    id name
}
");

            var qr = tree.ExecuteQuery(new TestSchema(), null);
            dynamic person = Enumerable.First((dynamic)qr.Data["people"]);
            // we only have the fields requested
            Assert.Equal(1, person.GetType().GetFields().Length);
            Assert.Equal("projects", person.GetType().GetFields()[0].Name);
        }

        [Fact]
        public void SupportsFragmentSelectionSyntax()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
query {
    people { ...info projects { id name } }
}
fragment info on Person {
    id name
}
");

            Assert.Single(tree.Operations.First().QueryFields);
            var qr = tree.ExecuteQuery(new TestSchema(), null);
            dynamic person = Enumerable.First((dynamic)qr.Data["people"]);
            // we only have the fields requested
            Assert.Equal(3, person.GetType().GetFields().Length);
            Assert.Equal("id", person.GetType().GetFields()[0].Name);
            Assert.Equal("name", person.GetType().GetFields()[1].Name);
            Assert.Equal("projects", person.GetType().GetFields()[2].Name);
        }

        [Fact]
        public void QueryWithUnknownArgument()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // Add a argument field with a require parameter
            var e = Assert.Throws<EntityGraphQLCompilerException>(() =>
            {
                var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
    query MyQuery($limit: Int = 10) {
        people(limit: $limit) { id name projects { id name } }
    }
    ");
            });
            Assert.Equal("No argument 'limit' found on field 'people'", e.Message);
        }

        [Fact]
        public void QueryWithDefaultArguments()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            schemaProvider.ReplaceField("people", new { limit = Required<int>() }, (db, p) => db.People.Take(p.limit), "List of people with limit");
            // Add a argument field with a require parameter
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
query MyQuery($limit: Int = 10) {
    people(limit: $limit) { id name projects { id name } }
}
");

            Assert.Single(tree.Operations.First().QueryFields);
            TestSchema context = new TestSchema();
            for (int i = 0; i < 20; i++)
            {
                context.People.Add(new Person());
            }
            var qr = tree.ExecuteQuery(context, null);
            dynamic people = (dynamic)qr.Data["people"];
            // we only have the fields requested
            Assert.Equal(10, Enumerable.Count(people));
        }

        [Fact]
        public void QueryWithDefaultArgumentsOverrideCodeDefault()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            // code default of 5
            schemaProvider.ReplaceField("people", new { limit = 5 }, (db, p) => db.People.Take(p.limit), "List of people with limit");

            // should use gql default of 6
            var tree = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
query MyQuery($limit: Int = 6) {
    people(limit: $limit) { id name projects { id name } }
}
");

            Assert.Single(tree.Operations.First().QueryFields);
            TestSchema context = new TestSchema();
            for (int i = 0; i < 20; i++)
            {
                context.People.Add(new Person());
            }
            var qr = tree.ExecuteQuery(context, null);
            dynamic people = (dynamic)qr.Data["people"];
            // we only have the fields requested
            Assert.Equal(6, Enumerable.Count(people));
        }
        [Fact]
        public void TestTopLevelScalar()
        {
            var schemaProvider = SchemaBuilder.FromObject<TestSchema>();
            var gql = new GraphQLCompiler(schemaProvider, new DefaultMethodProvider()).Compile(@"
query {
    totalPeople
}");

            var context = new TestSchema();
            context.People.Clear();
            for (int i = 0; i < 15; i++)
            {
                context.People.Add(new Person());
            }
            var qr = gql.ExecuteQuery(context, null);
            dynamic totalPeople = (dynamic)qr.Data["totalPeople"];
            // we only have the fields requested
            Assert.Equal(15, totalPeople);
        }
        [Fact]
        public void QueryWithEnumAsArg()
        {
            var schema = SchemaBuilder.FromObject<TestSchema>();
            schema.AddEnum("UserType", typeof(UserType), "Testing enums!");
            schema.ReplaceField("users", new
            {
                enumVal = (UserType?)null,
            },
            (db, p) => db.Users, "Testing enum");

            var gql = new GraphQLCompiler(schema, new DefaultMethodProvider()).Compile(@"
query {
    users(enumVal: Admin)
}");
            var context = new TestSchema();
            var qr = gql.ExecuteQuery(context, null);
            dynamic users = (dynamic)qr.Data["users"];
            // we only have the fields requested
            Assert.Equal(2, Enumerable.Count(users));
        }

        private enum UserType
        {
            Admin,
            User
        }
        private class TestSchema
        {
            public int TotalPeople { get => People.Count(); }
            public string Hello { get { return "returned value"; } }
            public List<Person> People { get; set; } = new List<Person> { new Person() };
            public IEnumerable<User> Users { get { return new List<User> { new User(9), new User(1, "2") }; } }
            public IEnumerable<Project> Projects { get { return new List<Project> { new Project() }; } }
        }

        private enum HeightUnit
        {
            Cm,
            Meter,
            Feet
        }

        private class User
        {
            public User(int id, string v = "1")
            {
                this.Id = id;
                this.Field2 = v;
            }

            public int Id { get; private set; }
            public int Field1 { get { return 2; } }
            public string Field2 { get; private set; }
            public Person Relation { get { return new Person(); } }
            public Task NestedRelation { get { return new Task(); } }
        }

        private class Person
        {
            public Guid Id { get { return new Guid("cccccccc-bbbb-4444-1111-ccddeeff0033"); } }
            public string Name { get { return "Luke"; } }
            public string LastName { get { return "Last Name"; } }
            public User User { get { return new User(100); } }
            public double Height { get { return 183.0; } }
            public IEnumerable<Project> Projects { get { return new List<Project> { new Project() }; } }

            public double GetHeight(HeightUnit unit)
            {
                return unit switch
                {
                    HeightUnit.Cm => Height,
                    HeightUnit.Meter => Height / 100,
                    HeightUnit.Feet => Height * 0.0328,
                    _ => throw new NotSupportedException($"Height unit {unit} not supported"),
                };
            }
        }
        private class Project
        {
            public Guid Id { get { return new Guid("aaaaaaaa-bbbb-4444-1111-ccddeeff0022"); } }
            public string Name { get { return "Project 3"; } }
            public IEnumerable<Task> Tasks { get { return new List<Task> { new Task() }; } }
        }
        private class Task
        {
            public int Id { get { return 33; } }
            public string Name { get { return "Task 1"; } }
        }
    }
}