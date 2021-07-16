namespace EntityGraphQL.Compiler
{
    /// <summary>
    /// Holds the BaseGraphQLField and it's compiled expressions (result from GetNodeExpression)
    /// </summary>
    public class CompiledField
    {
        public CompiledField(BaseGraphQLField field, ExpressionResult expression)
        {
            Field = field;
            Expression = expression;
        }

        public BaseGraphQLField Field { get; set; }
        public ExpressionResult Expression { get; set; }
    }
}