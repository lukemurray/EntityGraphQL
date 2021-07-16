# 0.69.0
- Added `SchemaProvider.UpdateType<T>(Action<SchemaType<T>> updateFunc)` to better help "contain" schema types. Instead of
```
schema.Type<MyType>().AddField(...);
schema.Type<MyType>().AddField(...);
schema.Type<MyType>().AddField(...);
```
we have
```
schema.UpdateType<MyType>(t => {
  t.AddField(...);
  t.AddField(...);
  t.AddField(...);
});
```
Similar for `SchemaProvider.AddType<T>(string name, string description, Action<SchemaType<T>> updateFunc)`

*Breaking changes*
- Big refactor/clean - hopefully easier to follow the post Antlr (compiled graphql) output - see `GraphQL*Field` classes
- Support for dotnet Entity Framework Core 3.1+ when using other services in the schema (`WithService()`)

To support EF 3.x as a base schema context we now build and execute expressions in 2 stages. See the updated readme section How EntityGraphQL handles WithService().

# 0.68.1
- Update Humanizer.Core dependency which resolves issue with newer dotnet core

# 0.68.0
- Fix issue where `FieldNamer` was not being consistently used. Thanks @AnderssonPeter
- Make sure we include inner exceptions on errors. Thanks @AnderssonPeter
- Added string and long parsing for DateTime and DateTimeOffset. Thanks @GravlLift

# 0.67.0
- As per GraphQL spec commas are optional (previously EntityGraphQL expected them in field/mutation arguments)

*Breaking changes*
- errors property on query result should not be present on the response if there are no errors per the graphQL specification.

# 0.66.1
- Fix bug with using `WithService()` when you require the schema context service again to create a link between services

# 0.66.0
- When using services other than the schema context in fields (that return a single object not a Enumerable) the methods/services are no longer executed multiple times. (issue #36). Notes below
- When a string matches a date time it will be converted to a `DateTime` object. Useful when using the `ArgumentHelper.EntityQuery` for advanced filtering. Regex matches `"yyyy-MM-dd HH:mm:ss.fffffffzzz"`, `"yyyy-MM-dd HH:mm:ss"`, `"yyyy-MM-dd"` with the separator between date and time being either ` ` or `T`
- `EntityQueryCompiler` (used in `ArgumentHelper.EntityQuery`) supports Enums
- `fieldNamer` used in mutations too

*Breaking changes*
- Cleaning up the API. The optional `isNullable` argument is removed from the `AddField()` methods. Use `IsNullable(bool)` method on the `Field` class or the `[GraphQLNotNull]` attribute.
- Cleaning up the API. `fieldNamer` argument removed from methods in `SchemaProvider` and `SchemaType`. Pass in a `fieldNamer` func to the constructor of `SchemaProvider` which will be used when it is auto creating fields. If you pass it in via `SchemaBuilder.FromObject` it will set it on the `SchemaProvider` created.

## Notes of services fix
If you build a field like so
```c#
schema.AddField("myField", ctx => WithService((IMyService srv) => srv.DoSomething(ctx)));

// Register the service with DI somewhere
public class MyService: IMyService {
  public SomeObject DoSomething(Context ctx)
  {
    // do something
    return data;
  }
}
```

With a query like
```gql
{
  myField { field1 field 2 }
}
```

We use to build an expression like so

```c#
srv.DoSomething(ctx) == null ? null : new {
  field1 = srv.DoSomething(ctx).field1,
  field2 = srv.DoSomething(ctx).field2
}
```

We now wrap this in a method call that only calls `DoSomething(ctx)` a single time Which looks like this
```c#
(ctx, srv) => NullCheckWrapper(srv.DoSomething(ctx), parameterValues, selection); // simplifying here

// Again a simified example of what NullCheckWrapper does
public object NullCheckWrapper(Expression<Func<Context, IMyService>> baseValue, object[] values, LambdaExpression selection)
{
  // null check
  if (baseValue == null)
    return null;
  // build the select on the object
  var result = selection.Compile().DynamicInvoke(baseValue);
}
```

This works with services used deeper inthe graph too. Example
```c#
schema.Type<Person>().AddField("complexField", (person) => DoSomething(person.Id));
```

GraphQL
```gql
{
  people {
    complexField {
      field1
      field1
    }
  }
}
```

The wrapped expression looks like this

```c#
(ctx, srv) => ctx.People.Select(person => new {
  complexField = NullCheckWrapper(srv.DoSomething(person.Id), parameterValues, selection); // simplifying here
})
```

This has been tested with EF Core and works well.

# 0.65.0
- You can now secure whole types in the schema. Add the `[GraphQLAuthorize("claim-name")]` to the class or use `schema.AddType(...).RequiresAllClaims("some-claim")`, `schema.AddType(...).RequiresAnyClaim("some-claim")`
- Add `GetField(Expression<Func<TBaseType, object>>)` overload
- operation name is optional for a `query` operation as per GraphQL spec if it is the only operation in the request
- Breaking - removed the `authorizeClaims` argument from `AddField()`. Please use `field.RequiresAllClaims("some-claim")`, `field.RequiresAnyClaim("some-claim")`

# 0.64.0
- Change - descriptions generated for a `.graphql` schema file now use the multiple line triple-quote `"""`
- Fix issue where an `WithService()` expression is wrapped in a `UnaryExpression` and we fail to get the lambda

# 0.63.0
- Expose a `SchemaProvider.ExecuteQueryAsync()`
- Fix #53 support mutations with no arguments
- With the above fix the context and/or the mutation arguments parameters are optional in your mutation method
- the parameters in the mutation methods are no longer required to follow a position
- `SchemaProvider.AddCustomScalarType()` is deprecated, use `AddScalarType`
- Directvies are now included in schema introspection
- Fix #52 - sometimes incorrect types generated for schema intropection or the GraphQL schema file format
- Refactor type information held in the schema. This mean return types etc are evaluated at schema creation time not execution. If you add a field that requires a type as an Arg ument or return type, that type must already be in the schema
- You can now provide a field namer function to name the generated fields when using `SchemaBuilder.FromObject()`, `ISchemaType.AddAllFields()` or `SchemaProvider.PopulateFromContext()`

*Breaking changes*
- The class that represents the mutation arguments must be marked with the `MutationArgumentsAttribute` either at the class level or the parameter
- `SchemaProvider` now adds a default `Date` scalar type in the schema that maps to/from the C# `DateTime` class. If you were previously adding that you'll get an error on type existing. Use `SchemaProvider.RemoveType<DateTime>()` to remove it and add it with a different name
- Type mapping information (`AddTypeMapping()`) are evaluated at schema creation time. You may need to add mappings before creating the rest of your schema

## 0.63.0-beta1 to 0.63.0-beta2
- Removed the empty `IMutationArguments` in favor for a `MutationArgumentsAttribute` on the parameter or the class

# 0.62.0
- Support async mutation methods

# 0.61.0
- Add model validation for mutation arguments. See updated readme
- Fix issue with services not correctly being included when the field is used in a fragment

# 0.60.0
- Add support for directives. Supported by default are `@include(if: Boolean!)` and `@skip(if: Boolean!)`. You can add your own that make changes to the expression pre-execution
- Added syntax support for subscription queries (they compile/no error but do not execute or work)
- Removed support for older syntax of complex queries that is not GQL standard
- Refactored `GraphQLVistor` and friends to make it easier to follow what is happening (I hope). See CONTRIBUTING.md for some notes
- From my testing the compiling and expression building is 15-20% faster than before (still network and or the DB calls are the largest)
- Allow `enum` values in the query schema (e.g. as an argument)
- Ignore static properties & fields on the object passed to `SchemaBuilder.FromObject` - they were not supported and threw errors anyway

# 0.50.1
- Name all `ParameterExpression`s as EF 3.1 expects a name (can throw an error)

# 0.50.0
- Fix claims check when required claims are empty
- Fix error message state which claims are required for the given access error

# 0.50.0-beta1
- Sorry about the quick turn around
- Breaking changes
  - Using DI inspired design now instead of the `TArg`
  - Use `WithService` helper to let EntityGraphQL know which services you require e.g. in an `AddField` use `(db, p) => WithService<IUserProvider>(users => users.Load(p.id))`
  - For mutations just add the services as arguments (at the end) as you typically do in dotnet
  - `schema.ExecuteQuery()` takes an `IServiceProvider` which it uses to look up required services like `IUserProvider` above

# 0.40.0
- Breaking changes
  - Trying to clean up the interface for careting a schema and allowing an easier way to get services in mutations and field selections
  - Rename `MappedSchemaProvider` to `SchemaProvider`
  - Remove extension `object.QueryObject()` and require a more specific call to `SchmeaProvider.ExecuteQuery()`
  - `SchmeaProvider.ExecuteQuery()` takes a `TArg` type which is an argument that will be passed to all field selections and mutation methods. This replaces `mutationArgs` in `object.QueryObject()` and lets both mutations and field selections access other services non-statically (e.g. via `TArg` being an `IServiceProvider`). See updated readme and demo project
  - `SchemaProvider` is now create using `SchemaBuilder.Create<TContext, TArg>()` or `SchemaBuilder.Create<TContext>()` giving you a short cut for not providing a `TArg`
  - `SchemaBuilder.FromObject<TContext, TArg>()` now takes second type argument for the `TArg` value. Also `SchemaBuilder.FromObject<TContext>()` shortcut
- Fix bug where EntityQuery arguments were being cached in the schema

# 0.32.1
- Add `ContextId` to the ignored list in `SchemaBuilder` for EF 3.1

# 0.32.0
- Clean up `LinqExtensions`, removing the overloads that take `LambdaExpression`s. Use `.AsQueryable().Where(someExpressionVar)` instead. It works better with EF and other LinqProviders.

# 0.31.0
- Breaking change - Multiple `[GraphQLAuthorize]` mean all polcies are required and supplying multiple in a single `[GraphQLAuthorize]` mean any
- Do not generate an empty mutation type if there are no mutations
- Fix query introspection to output mapped types correctly
- Support multiple queries in a request with the operation name of which one to run - e.g. how GraphiQL handles multiple queries
- Update error messages
- Clean up some APIs - `SchemaType.AddAllFields()`, `Field.RequiresAllClaims()`, `Field.RequiresAnyClaim()`

# 0.30.0
- Initial support for Authorization and Security in the schema - see updated readme and provide feedback

# 0.29.0
- Support `fragment` statements in other positions in the query

# 0.28.2
- Fix error with `EntityQueryType<>` as a field argument not being defined as a `String` in introspection

# 0.28.1
 - Fix issue introduced in 0.28 when using the `RequiredField<>` type

# 0.28.0
- Only convert a string matching a `Guid` when the arg type is a `Guid` or `Guid?`

# 0.27.2
- Fix issue where a non-required EntityQueryType Filter throw an error if it wasn't supplied

# 0.27.1
- Better support mutations that return an object, not a list

# 0.27.0
- Introspection query `__type(name: "")` now correctly returns an object not an array
- `[Description("")]` attributes on `enum` fields are now read into the schema
- Fix issue where introspection query would have dupelicate types for enum types

# 0.26.0
- `ISchemaType.AddAllFields` requires a schema as it can add newly discovered types to that schema
- `ISchemaType.AddAllFields` by default adds new `enum` types to the schema if found as a field type
- `ISchemaType.AddAllFields` can (off by default) add new complex types to the schema if found as a field type
- `Schema.Type<TType>()` now searches by `TType` not `typeof(TType).Name`. Allowing you to add a type with a different name but still get the typed `SchemaType<T>` back

# 0.25.0
- Add the ability to add enum types to the schema
- The auto schema builder now adds enum types it finds to the schema by default
- Enum values are referenced by the string value, if using JSON.NET you will want to use `[JsonConverter(typeof(StringEnumConverter))]`

# 0.24.0
- Add `GraphQLNotNullAttribute` to mark fields as not nullable in the graphql schema
- By default when generating a `.schema` file `IEnumerable<T>` will generate the element type as not nullable. E.g. `[T!]`. Use `GraphQLElementTypeNullableAttribute` to mark it that the list can contain null items
- Support mapping `decimal` to `number`
- Better support for defining nullable or non-nullable types

# 0.23.3
- Allow adding a mapping type to a type that already exists in the schema. E.g. you might add an input type `Point` and want to map the dotnet type `Point` to it.

# 0.23.2
- fix issue with required type in an array e.g. `[ID!]`

# 0.23.1
- fix issue with generated required types

# 0.23.0
- Output fields as required in .graphql schemas

# 0.22.0
- You can now specify if a field is ignored for queries, mutations or both (if you're sharing DTOs/objects)
- Don't output the meta information of the schema in the schema definition
- Prevent duplicate scalar types in schema generation
- Fix issue where `mutation` keyword could not have any white space before it

# 0.21.0
- Change, make ENUMs a `Int` type as dotnet serialises them like that
- Fix input types missing for the SDL schema generated
- Fix issue where mutation args has repeating `ofType` data in introspection
- Fix issue where InputTypes would be duplicated in introspection query results
- Fix issue where `ToList()` was being called deep in expressions causing issues with EF

# 0.20.0
- Add the ability to add custom scalar types to the schema
- Fix a bug where introspection queries were in correct if you have a List/Array in your mutation type

# 0.19.1
- Fix a bug where mutation arg objects retained values from previous mutation - i.e if the next call to that mutation didn't provide some optional arguments

# 0.19.0
- `QueryObject()` calls `ToList()` on any lists so if you are using something like EF all queries will be evaluated on `QueryObject()`, not just the ones that return a single object (as they call `FirstOrDefault()`). This is more consistent, the result contains all your data (regardless of underlying ORM or not) and not a miz of evaluated and non-evaluated.
- Add `RemoveTypeAndAllFields()` on `MappedSchemaProvider` to more easily clean up a schema that was auto created by `SchemaBuilder.FromObject<T>()`

# 0.18.4
- Fix case matching of arguments in mutations. EntityGraphQL defaults to turning dotnet `UpperCaseFieldsAndProperties` to `camelCaseFieldsAndProperties` as this was the intention with the change in 0.18.0
- Enum values are as defined. E.g. if you have an enum `Meter` you can use `"Meter"` not `"meter"`

# 0.18.3
- map Int16 and UInt16 to Int
- Fix issue where argument names were not case sensitive (the Breaking change introduced in 0.18)

# 0.18.2
- Fix `kind` in schema introspection query to not have `!`

# 0.18.1
- Update dependences (JSON.NET)
- Fix some small casing issues

# 0.18.0
- Support for schema introspection. Top two fields (`__schema` and `__type(name: String!)`) are implemented. There are some things missing where we currently don't support the feature (directives). GraphiQL introspection query executes and you can naviagte the example. Big thanks to @JTravis76 for starting the work.
- Implement #18 default argument values in GQL operations `mutation MyMutation($arg: String = "hey defaults") { ... }`
- Breaking change - If you purely use `SchemaBuilder.FromObject()` it now creates all field names `lowerCaseCamel` and type names `UpperCamelCase` like the GraphQL defaults. Also since GraphQL is case sensitive I have enforced that. You _may_ need to update some queries to match casing

# 0.17.0
- Add the option to have other parameters passed into a mutation method. Very useful for IServiceProvider or other services your mutations require

# 0.16.2
- Fix issue where duplicate fields (say from a query and a fragment) would cause an error

# 0.16.1
- Bring inner exception details up
- Fix issue where fields that require arguments fail (See Paging example in Demo)

# 0.16.0
- Add support for full queries on a mutation result. See readme for more details (you return an Expression)
- Initial support for GraphQL fragments. fix #2
- Fix issues with using field arguments deeper in the graph schema
- Add support for `operationName` in the `GraphQLRequest`
- Add support for graphql comments
- Add GraphiQL web interface to the demo app
- Add `GraphQLIgnore` attribute to ignore a field on a Type for the automatic schema builder
- fix bug where it would sometimes try to give an `UInt64` to an argument that required an `Int32`
- (beta) Add `dotnet gql` command to generate a schema from your current DBContext. See readme for more info

# 0.15.8
- Add `WhereWhen()` to the extension helpers

# 0.15.7
- Fix bug in handling converting a string into an enum
- Fix issue looking up types internally when working with anonymous types

# 0.15.6
- Fix issue with incorrectly checking for generic types

# 0.15.5
- Fix issue with array types in the dynamically generated type for lambdas

# 0.15.4
- Fix issue where the generated GraphQL schema had the incorrect type for arrays (e.g. double[])
- Fix issue with `SchemaBuilder.FromObject` building invalid types from arrays.

# 0.15.3
- Fix an issue where it would generate an invalid field name if we couldn't singularize the name

# 0.15.2
- Move the query results into a `QueryResult` class so it is easier to work with

# 0.15.1
- Fix #11 - failure to use `RequiredField` with types like `uint` - it would try to initialise it with an `int`

# 0.15
- Remove old code that supported multiple fields with the same name and different arguments. GrpahQL doesn't support that and it caused a bug where it would sometimes not find the field you want. You can implment optional arguments and build a complex field like `schemaProvider.AddField("myField", new {name = (string)null, id = (Guid?)null}, (db, param) => param.id.HasValue ? db.MyEntities.Where(l => l.Id == param.id).FirstOrDefault() : db.MyEntities.Where(l => l.Name == param.name).FirstOrDefault(), "Returns an Entity object by ID or a match on the name argument");`

# 0.14.4
- Allow user to supply a type mapping for the generated GraphQL schema

# 0.14.3
- Fix an issue where we would some time identify a type as IEnumerable when it shouldn't have been
- Allow `ReplaceField` on a sub type

# 0.14.2
- Convert enums correctly

# 0.14.1
- Fix SchemaGenerator to support arrays better
- Support turning `QueryRequest` variables from JSON objects into their requied objects

# 0.14.0
- Bring back `ReplaceField` as technically GraphQL doesn't support overloading, so if you're using tools like Apollo etc to generate code you need to only have unique fields
- Added `GetGraphQLSchema` on the schema to return a `.graphql` schema file for input into tools like Apollo codegen
- Add the option to add descriptions to Mutations

# 0.13.1
- Fix issue where operation name with no arguments failed

# 0.13.0
- Breaking change: Strings are now defined by double quotes `"` to fit in with GraphQL better
- Added support to use a LINQ-style language as a argument in GraphQL. E.g the following GQL field, `{ users(filter: "active = true and age > 20") { id name } }` can be defined with `schema.AddField("users", new {filter = EntityQuery<User>()}, (ctx, p) => ctx.Users.Where(p.filter), "Return filtered users")`. Letting you write that filter using any fields defined on the `User` object
- Add `null`/`empty` constant for checking for null in EQL

# 0.12.1
- Fix bug converting Int values to double? values

# 0.12.0
- Support field "overloads" - fields with same name and different arguments.

# 0.11.0
- Renamed package EntityGraphQL to emphasizes that it implements GraphQL

# 0.10.1
- Fix issue with arrays and objects in variables parameter on the request
- Better error messages when GraphQL arguments can't be mapped to an object
- Validate required variables in operations are supplied in the variables dictionary
- Fix potential issue with selecting sub object graphs

# 0.10.0
- Change the errors to appear in `errors` key along side `data` key as per GraphQL
- Fix an issue when selecting fields from a mutation result

# 0.9.4
- Support selecting an object that may be null `{ person {name} }` `person` will be null if it does not exist

# 0.9.3
- Parameter types can be optional (no `!`)

# 0.9.2
- Fix type issues with mutation args
- Change auto entity call with `id:` (e.g. `{ entity(id: 13)}`) to call the `First()` method last
- Fix issue with selecting a one-to-one relation in a query

# 0.9.1
- Fix a issue where schema and metrhod provider were not being passed down in `QueryObject`

# 0.9.0
- Add initial support for Mutations
- `,` between fields is optional like in GraphQL
- Support `__typename` metadata

# 0.8.0
- `Moved EntityQueryLanguage.DataApi` namespace to `EntityQueryLanguage.GraphQL` to better communicate that its intent is to support
- Add support GraphQL arguments in fields. See updated readme
- By default `SchemaBuilder.FromObject<TType>()` generates a non-pural field for any type with a public `Id` property, with the argument name of `id`. E.g. A field `people` that returns a `IEnumerable<Person>` will result in a `person(id)` field
- Move results to field `data` in the resulting object to match GraphQL
- Support the `query` keyword for graphql

# 0.7.0
- Added support for complex nested queries when using the `EFRelationshipHandler`
- Fix type caching issue causing exception on changing a sub query