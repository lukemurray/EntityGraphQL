using System.Linq;
using System.Text;

namespace EntityGraphQL.Schema
{
    public class SchemaGenerator
    {
        internal static string EscapeString(string input)
        {
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        internal static string Make(ISchemaProvider schema)
        {
            var scalars = new StringBuilder();

            foreach (var item in schema.GetScalarTypes().Distinct())
            {
                scalars.AppendLine($"scalar {item.Name}");
            }

            var enums = BuildEnumTypes(schema);
            var types = BuildSchemaTypes(schema);
            var mutations = BuildMutations(schema);
            var hasMutations = mutations.Any();

            var queryTypes = MakeQueryType(schema);

            var schemaStr = $@"schema {{
    query: RootQuery
    {(hasMutations ? "mutation: Mutation" : "")}
}}

{scalars}
{enums}

type RootQuery {{
{queryTypes}
}}
{types}
";
            if (hasMutations)
            {
                schemaStr += $@"type Mutation {{
{mutations}
}}";
            }
            return schemaStr;
        }

        private static string BuildMutations(ISchemaProvider schema)
        {
            var mutations = new StringBuilder();
            foreach (var item in schema.GetMutations())
            {
                if (!string.IsNullOrEmpty(item.Description))
                    mutations.AppendLine($"\t\"\"\"{EscapeString(item.Description)}\"\"\"");

                mutations.AppendLine($"\t{schema.SchemaFieldNamer(item.Name)}{GetGqlArgs(schema, item, "")}: {item.ReturnType.GqlTypeForReturnOrArgument}");
            }

            return mutations.ToString();
        }

        private static string BuildEnumTypes(ISchemaProvider schema)
        {
            var types = new StringBuilder();
            foreach (var typeItem in schema.GetNonContextTypes())
            {
                if (typeItem.Name.StartsWith("__") || !typeItem.IsEnum)
                    continue;

                types.AppendLine();
                if (!string.IsNullOrEmpty(typeItem.Description))
                    types.AppendLine($"\t\"\"\"{EscapeString(typeItem.Description)}\"\"\"");

                types.AppendLine($"enum {typeItem.Name} {{");
                foreach (var field in typeItem.GetFields())
                {
                    if (field.Name.StartsWith("__"))
                        continue;

                    if (!string.IsNullOrEmpty(field.Description))
                        types.AppendLine($"\t\"\"\"{EscapeString(field.Description)}\"\"\"");

                    types.AppendLine($"\t{field.Name}");

                }
                types.AppendLine("}");
            }

            return types.ToString();
        }

        private static string BuildSchemaTypes(ISchemaProvider schema)
        {
            var types = new StringBuilder();
            foreach (var typeItem in schema.GetNonContextTypes())
            {
                if (typeItem.Name.StartsWith("__") || typeItem.IsEnum || typeItem.IsScalar)
                    continue;

                types.AppendLine();
                if (!string.IsNullOrEmpty(typeItem.Description))
                    types.AppendLine($"\"\"\"{EscapeString(typeItem.Description)}\"\"\"");

                types.AppendLine($"{(typeItem.IsInput ? "input" : "type")} {typeItem.Name} {{");
                foreach (var field in typeItem.GetFields())
                {
                    if (field.Name.StartsWith("__"))
                        continue;

                    if (!string.IsNullOrEmpty(field.Description))
                        types.AppendLine($"\t\"\"\"{EscapeString(field.Description)}\"\"\"");

                    types.AppendLine($"\t{schema.SchemaFieldNamer(field.Name)}{GetGqlArgs(schema, field)}: {field.ReturnType.GqlTypeForReturnOrArgument}");
                }
                types.AppendLine("}");
            }

            return types.ToString();
        }

        private static object GetGqlArgs(ISchemaProvider schema, IField field, string noArgs = "")
        {
            if (field.Arguments == null || !field.Arguments.Any())
                return noArgs;

            var all = field.Arguments.Select(f => schema.SchemaFieldNamer(f.Key) + ": " + f.Value.Type.GqlTypeForReturnOrArgument);

            var args = string.Join(", ", all);
            return string.IsNullOrEmpty(args) ? "" : $"({args})";
        }

        private static string MakeQueryType(ISchemaProvider schema)
        {
            var sb = new StringBuilder();

            foreach (var t in schema.GetQueryFields().OrderBy(s => s.Name))
            {
                if (t.Name.StartsWith("__"))
                    continue;
                if (!string.IsNullOrEmpty(t.Description))
                    sb.AppendLine($"\t\"\"\"{EscapeString(t.Description)}\"\"\"");
                sb.AppendLine($"\t{schema.SchemaFieldNamer(t.Name)}{GetGqlArgs(schema, t)}: {t.ReturnType.GqlTypeForReturnOrArgument}");
            }

            return sb.ToString();
        }
    }
}