using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using EntityGraphQL.Extensions;

namespace EntityGraphQL.Compiler.Util
{
    /// <summary>
    /// As people build schema fields they may be against different parameters, this visitor lets us change it to the one used in compiling the EQL.
    /// I.e. in (p_0) => p_0.blah `p_0` needs to be the same object otherwise it expects 2 values passed in.
    /// </summary>
    public class ParameterReplacer : ExpressionVisitor
    {
        private Expression newParam;
        private Type toReplaceType;
        private ParameterExpression toReplace;
        private string newFieldName;
        private bool finished;

        /// <summary>
        /// Rebuilds the expression by replacing toReplace with newParam. Optionally looks for newFieldName as it is rebuilding.
        /// Used when rebuilding expressions to happen from the sans-services results
        /// </summary>
        /// <param name="node"></param>
        /// <param name="toReplace"></param>
        /// <param name="newParam"></param>
        /// <param name="newFieldName"></param>
        /// <returns></returns>
        public Expression Replace(Expression node, ParameterExpression toReplace, Expression newParam, string newFieldName = null)
        {
            this.newParam = newParam;
            this.toReplace = toReplace;
            this.toReplaceType = null;
            this.newFieldName = newFieldName;
            finished = false;
            return Visit(node);
        }

        public Expression ReplaceByType(Expression node, Type toReplaceType, Expression newParam)
        {
            this.newParam = newParam;
            this.toReplaceType = toReplaceType;
            this.toReplace = null;
            this.newFieldName = null;
            finished = false;
            return Visit(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (toReplace != null && toReplace == node)
                return newParam;
            if (toReplaceType != null && node.NodeType == ExpressionType.Parameter && toReplaceType == node.Type)
                return newParam;
            return node;
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            var p = node.Parameters.Select(base.Visit).Cast<ParameterExpression>();
            var body = base.Visit(node.Body);
            return Expression.Lambda(body, p);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            // returned expression may have been modified and we need to rebuild
            if (node.Expression != null)
            {
                Expression nodeExp;
                if (node.Expression.Type == toReplaceType)
                    nodeExp = newParam;
                else
                    nodeExp = base.Visit(node.Expression);

                if (finished)
                    return nodeExp;


                if (newFieldName != null)
                {
                    var newField = nodeExp.Type.GetField(newFieldName);
                    if (newField != null)
                    {
                        finished = true;
                        nodeExp = Expression.Field(nodeExp, newField);
                    }
                    else
                        nodeExp = Expression.PropertyOrField(nodeExp, node.Member.Name);
                }
                else
                    nodeExp = Expression.PropertyOrField(nodeExp, node.Member.Name);
                return nodeExp;
            }
            return base.VisitMember(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object == null && node.Arguments.Count > 1 && node.Method.IsGenericMethod)
            {
                // Replace expression that are inside method calls that might need parameters updated (.Where() etc.)
                var callBase = base.Visit(node.Arguments[0]);
                var callBaseType = callBase.Type.IsEnumerableOrArray() ? callBase.Type.GetEnumerableOrArrayType() : callBase.Type;
                var oldCallBaseType = node.Arguments[0].Type.IsEnumerableOrArray() ? node.Arguments[0].Type.GetEnumerableOrArrayType() : node.Arguments[0].Type;
                if (callBaseType != oldCallBaseType)
                {
                    var replaceAgain = new ParameterReplacer();
                    var newTypeArgs = new List<Type> { callBaseType };
                    var newArgs = new List<Expression>();
                    var oldTypeArgs = node.Method.GetGenericArguments();
                    foreach (var oldArg in node.Arguments.Skip(1))
                    {
                        var newArg = replaceAgain.ReplaceByType(oldArg, oldCallBaseType, Expression.Parameter(callBaseType));
                        newArgs.Add(newArg);
                        if (oldTypeArgs.Contains(oldArg.Type))
                            newTypeArgs.Add(newArg.Type);
                        else if (newArg.NodeType == ExpressionType.Lambda && oldTypeArgs.Contains(((LambdaExpression)newArg).ReturnType))
                            newTypeArgs.Add(((LambdaExpression)newArg).ReturnType);
                    }
                    if (oldTypeArgs.Length != newTypeArgs.Count)
                        throw new EntityGraphQLCompilerException($"Post service object selection contains a method call with mismatched generic type arguments.");
                    var newCall = Expression.Call(node.Method.DeclaringType, node.Method.Name, newTypeArgs.ToArray(), (new[] { callBase }).Concat(newArgs).ToArray());
                    return newCall;
                }
            }
            return base.VisitMethodCall(node);
        }
    }
}