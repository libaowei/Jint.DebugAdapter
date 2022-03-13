﻿using Jint.Native;
using Jint.Native.Argument;
using Jint.Native.Array;
using Jint.Native.Function;
using Jint.Native.Object;
using Jint.Native.TypedArray;
using Jint.Runtime.Debugger;
using Jint.Runtime.Descriptors;

namespace Jint.DebugAdapter.Variables
{
    public class VariableStore
    {
        private readonly Engine engine;
        private int nextId = 1;
        private readonly Dictionary<int, VariableContainer> containers = new();

        public VariableStore(Engine engine)
        {
            this.engine = engine;
        }

        public int Add(DebugScope scope, CallFrame frame = null)
        {
            var container = new ScopeVariableContainer(this, nextId++, scope, frame);
            return Add(container);
        }

        public int Add(ObjectInstance instance)
        {
            var container = new ObjectVariableContainer(this, nextId++, instance);
            return Add(container);
        }

        public int AddArrayLike(ObjectInstance instance)
        {
            var container = new ArrayLikeVariableContainer(this, nextId++, instance);
            return Add(container);
        }

        public int Add(PropertyDescriptor prop, ObjectInstance owner)
        {
            var container = new PropertyVariableContainer(this, nextId++, prop, owner, engine);
            return Add(container);
        }

        private int Add(VariableContainer container)
        {
            containers.Add(container.Id, container);
            return container.Id;
        }

        public VariableContainer GetContainer(int id)
        {
            return containers[id];
        }

        public ValueInfo CreateValue(string name, JsValue value)
        {
            return value switch
            {
                null => new NullValueInfo(name),
                JsNull or
                JsString or
                JsNumber or
                JsBigInt or
                JsBoolean or
                JsUndefined or
                JsSymbol or
                JsNull => new PrimitiveValueInfo(name, value),
                ArgumentsInstance arr => new ArrayValueInfo(name, arr) { VariablesReference = AddArrayLike(arr) },
                ArrayInstance arr => new ArrayValueInfo(name, arr) { VariablesReference = AddArrayLike(arr) },
                TypedArrayInstance arr => new ArrayValueInfo(name, arr) { VariablesReference = AddArrayLike(arr) },
                FunctionInstance func => new FunctionValueInfo(name, func),
                ObjectInstance obj => new ObjectValueInfo(name, obj) { VariablesReference = Add(obj) },
                _ => throw new NotImplementedException($"Unimplemented JsValue type: {value.GetType()}")
            };
        }

        public ValueInfo CreateValue(string name, PropertyDescriptor prop, ObjectInstance owner)
        {
            if (prop.Get != null)
            {
                return new GetterValueInfo(name)
                {
                    // Add a variable reference for lazy evaluation of the getter
                    VariablesReference = Add(prop, owner),
                };
            }
            else
            {
                return CreateValue(name, prop.Value);
            }
        }

        public ValueInfo SetValue(int variablesReference, string name, JsValue value)
        {
            var container = containers.GetValueOrDefault(variablesReference);
            if (container == null)
            {
                throw new VariableException($"Unknown parent variables reference: {variablesReference}");
            }

            var newValue = container.SetVariable(name, value);
            return CreateValue(name, newValue);
        }

        public void Clear()
        {
            containers.Clear();
        }
    }
}
