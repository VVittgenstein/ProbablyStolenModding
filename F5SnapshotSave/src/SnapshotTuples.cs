using System;
using System.Collections.Generic;

namespace F5SnapshotSave
{
    // Typed access to public tuple data; no reflection over System members.
    internal interface ITupleFields
    {
        object Create();
        object Get(object target, string field);
        object Set(object target, string field, object value);
    }
    internal static class SnapshotTuples
    {
        private static readonly Type[] types = { typeof(ValueTuple<>), typeof(ValueTuple<,>), typeof(ValueTuple<,,>), typeof(ValueTuple<,,,>), typeof(ValueTuple<,,,,>), typeof(ValueTuple<,,,,,>), typeof(ValueTuple<,,,,,,>), typeof(ValueTuple<,,,,,,,>) };
        private static readonly Type[] helpers = { typeof(TupleFields<>), typeof(TupleFields<,>), typeof(TupleFields<,,>), typeof(TupleFields<,,,>), typeof(TupleFields<,,,,>), typeof(TupleFields<,,,,,>), typeof(TupleFields<,,,,,,>), typeof(TupleFields<,,,,,,,>) };
        private static readonly Dictionary<Type, ITupleFields> cache = new Dictionary<Type, ITupleFields>();
        internal static bool IsTuple(Type type) { return type.IsGenericType && Array.IndexOf(types, type.GetGenericTypeDefinition()) >= 0; }
        internal static ITupleFields Access(Type type)
        {
            ITupleFields value;
            if (!cache.TryGetValue(type, out value))
            {
                int index = Array.IndexOf(types, type.GetGenericTypeDefinition());
                if (index < 0) throw new NotSupportedException(type.FullName);
                value = (ITupleFields)Activator.CreateInstance(helpers[index].MakeGenericType(type.GetGenericArguments()));
                cache[type] = value;
            }
            return value;
        }
        internal static List<SnapshotField> Fields(Type type)
        {
            var result = new List<SnapshotField>();
            Type[] args = type.GetGenericArguments();
            for (int i = 0; i < args.Length; i++)
                result.Add(new SnapshotField(type, i == 7 ? "Rest" : "Item" + (i + 1), false, false, args[i].IsValueType, args[i].FullName));
            return result;
        }
    }
    internal sealed class TupleFields<T1> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2, T3>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3, T4> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2, T3, T4>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3, T4>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                case "Item4": return value.Item4;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3, T4>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                case "Item4": value.Item4 = (T4)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3, T4, T5> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2, T3, T4, T5>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                case "Item4": return value.Item4;
                case "Item5": return value.Item5;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                case "Item4": value.Item4 = (T4)item; break;
                case "Item5": value.Item5 = (T5)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3, T4, T5, T6> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2, T3, T4, T5, T6>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                case "Item4": return value.Item4;
                case "Item5": return value.Item5;
                case "Item6": return value.Item6;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                case "Item4": value.Item4 = (T4)item; break;
                case "Item5": value.Item5 = (T5)item; break;
                case "Item6": value.Item6 = (T6)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3, T4, T5, T6, T7> : ITupleFields
    {
        public object Create() { return default(ValueTuple<T1, T2, T3, T4, T5, T6, T7>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6, T7>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                case "Item4": return value.Item4;
                case "Item5": return value.Item5;
                case "Item6": return value.Item6;
                case "Item7": return value.Item7;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6, T7>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                case "Item4": value.Item4 = (T4)item; break;
                case "Item5": value.Item5 = (T5)item; break;
                case "Item6": value.Item6 = (T6)item; break;
                case "Item7": value.Item7 = (T7)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
    internal sealed class TupleFields<T1, T2, T3, T4, T5, T6, T7, T8> : ITupleFields where T8 : struct
    {
        public object Create() { return default(ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>); }
        public object Get(object target, string field)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>)target;
            switch (field)
            {
                case "Item1": return value.Item1;
                case "Item2": return value.Item2;
                case "Item3": return value.Item3;
                case "Item4": return value.Item4;
                case "Item5": return value.Item5;
                case "Item6": return value.Item6;
                case "Item7": return value.Item7;
                case "Rest": return value.Rest;
                default: throw new MissingMemberException(field);
            }
        }
        public object Set(object target, string field, object item)
        {
            var value = (ValueTuple<T1, T2, T3, T4, T5, T6, T7, T8>)target;
            switch (field)
            {
                case "Item1": value.Item1 = (T1)item; break;
                case "Item2": value.Item2 = (T2)item; break;
                case "Item3": value.Item3 = (T3)item; break;
                case "Item4": value.Item4 = (T4)item; break;
                case "Item5": value.Item5 = (T5)item; break;
                case "Item6": value.Item6 = (T6)item; break;
                case "Item7": value.Item7 = (T7)item; break;
                case "Rest": value.Rest = (T8)item; break;
                default: throw new MissingMemberException(field);
            }
            return value;
        }
    }
}
