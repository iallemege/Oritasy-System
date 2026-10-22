using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Oritasy
{
    internal static class FastField
    {
        internal static Func<T, TVal> Get<T, TVal>(FieldInfo field)
        {
            if (field == null)
                return null;
            ParameterExpression obj = Expression.Parameter(typeof(T), "o");
            Expression inst = Instance(obj, field);
            Expression body = Expression.Field(inst, field);
            if (body.Type != typeof(TVal))
                body = Expression.Convert(body, typeof(TVal));
            return Expression.Lambda<Func<T, TVal>>(body, obj).Compile();
        }

        internal static Action<T, TVal> Set<T, TVal>(FieldInfo field)
        {
            if (field == null || field.IsInitOnly)
                return null;
            ParameterExpression obj = Expression.Parameter(typeof(T), "o");
            ParameterExpression val = Expression.Parameter(typeof(TVal), "v");
            Expression inst = Instance(obj, field);
            Expression rhs = val;
            if (val.Type != field.FieldType)
                rhs = Expression.Convert(val, field.FieldType);
            return Expression.Lambda<Action<T, TVal>>(
                Expression.Assign(Expression.Field(inst, field), rhs), obj, val).Compile();
        }

        static Expression Instance(ParameterExpression obj, FieldInfo field)
        {
            if (field.IsStatic)
                return null;
            if (field.DeclaringType == obj.Type)
                return obj;
            return Expression.Convert(obj, field.DeclaringType);
        }
    }
}
