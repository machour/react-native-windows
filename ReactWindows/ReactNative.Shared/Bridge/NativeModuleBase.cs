using Newtonsoft.Json.Linq;
using ReactNative.Bridge.Queue;
using ReactNative.Tracing;
using System;
using System.Collections.Generic;
using System.Reflection;
using static System.FormattableString;

namespace ReactNative.Bridge
{
    /// <summary>
    /// Base class for React Native modules. Implementations can be linked
    /// to lifecycle events, such as the creation and disposal of the
    /// <see cref="IReactInstance"/> by overriding the appropriate methods.
    /// 
    /// Native methods are exposed to JavaScript with the
    /// <see cref="ReactMethodAttribute"/> annotation. These methods may only
    /// use arguments that can be parsed by <see cref="JToken.ToObject{T}()"/> or
    /// <see cref="ICallback"/>, which maps from a JavaScript function and can
    /// be used only as a last parameter, or in the case of success and error
    /// callback pairs, the last two arguments respectively.
    /// 
    /// All methods annotated with <see cref="ReactMethodAttribute"/> must
    /// return <see cref="void"/>.
    /// 
    /// Please note that it is not allowed to have multiple methods annotated
    /// with <see cref="ReactMethodAttribute"/> that share the same name.
    /// </summary>
    /// <remarks>
    /// Default implementations of <see cref="Initialize"/> and 
    /// <see cref="OnReactInstanceDispose"/> are provided for convenience.
    /// Subclasses need not call these base methods should they choose to
    /// override them.
    /// </remarks>
    public abstract class NativeModuleBase : INativeModule
    {
        private static readonly IReactDelegateFactory s_defaultDelegateFactory =
#if WINDOWS_UWP
            ReflectionReactDelegateFactory.Instance;
#else
            CompiledReactDelegateFactory.Instance;
#endif

        private static readonly IReadOnlyDictionary<string, object> s_emptyConstants
            = new Dictionary<string, object>();

        private readonly IReadOnlyDictionary<string, INativeMethod> _methods;
        private readonly IReactDelegateFactory _delegateFactory;
        private readonly IActionQueue _actionQueue;

        /// <summary>
        /// Instantiates a <see cref="NativeModuleBase"/>.
        /// </summary>
        protected NativeModuleBase()
            : this(s_defaultDelegateFactory)
        {
        }

        /// <summary>
        /// Instantiates a <see cref="NativeModuleBase"/>.
        /// </summary>
        /// <param name="delegateFactory">
        /// Factory responsible for creating delegates for method invocations.
        /// </param>
        protected NativeModuleBase(IReactDelegateFactory delegateFactory)
            : this(delegateFactory, null)
        {
        }

        /// <summary>
        /// Instantiates a <see cref="NativeModuleBase"/>.
        /// </summary>
        /// <param name="actionQueue">
        /// The action queue that native modules should execute on.
        /// </param>
        protected NativeModuleBase(IActionQueue actionQueue)
            : this(s_defaultDelegateFactory, actionQueue)
        {
        }

        /// <summary>
        /// Instantiates a <see cref="NativeModuleBase"/>.
        /// </summary>
        /// <param name="delegateFactory">
        /// Factory responsible for creating delegates for method invocations.
        /// </param>
        /// <param name="actionQueue">
        /// The action queue that native modules should execute on.
        /// </param>
        protected NativeModuleBase(IReactDelegateFactory delegateFactory, IActionQueue actionQueue)
        {
            _delegateFactory = delegateFactory;
            _methods = InitializeMethods();
            _actionQueue = actionQueue;
        }

        /// <summary>
        /// Return true if you intend to override some other native module that
        /// was registered, e.g., as part of a different package (such as the
        /// core one). Trying to override without returning true from this 
        /// method is considered an error and will throw an exception during
        /// initialization. By default, all modules return false.
        /// </summary>
        public virtual bool CanOverrideExistingModule
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// The action queue used by the native module.
        /// </summary>
        public IActionQueue ActionQueue
        {
            get
            {
                return _actionQueue;
            }
        }

        /// <summary>
        /// The constants exported by this module.
        /// </summary>
        public virtual IReadOnlyDictionary<string, object> Constants
        {
            get
            {
                return s_emptyConstants;
            }
        }

        /// <summary>
        /// The methods callabke from JavaScript on this module.
        /// </summary>
        public IReadOnlyDictionary<string, INativeMethod> Methods
        {
            get
            {
                if (_methods == null)
                {
                    throw new InvalidOperationException("Module has not been initialized.");
                }

                return _methods;
            }
        }

        /// <summary>
        /// The name of the module.
        /// </summary>
        /// <remarks>
        /// This will be the name used to <code>require()</code> this module
        /// from JavaScript.
        /// </remarks>
        public abstract string Name
        {
            get;
        }

        /// <summary>
        /// Called after the creation of a <see cref="IReactInstance"/>, in
        /// order to initialize native modules that require the React or
        /// JavaScript modules.
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// Called before a <see cref="IReactInstance"/> is disposed.
        /// </summary>
        public virtual void OnReactInstanceDispose()
        {
        }

        private IReadOnlyDictionary<string, INativeMethod> InitializeMethods()
        {
            var declaredMethods = GetType().GetTypeInfo().DeclaredMethods;
            var exportedMethods = new List<MethodInfo>();
            foreach (var method in declaredMethods)
            {
                if (method.IsDefined(typeof(ReactMethodAttribute)))
                {
                    exportedMethods.Add(method);
                }
            }

            var methodMap = new Dictionary<string, INativeMethod>(exportedMethods.Count);
            foreach (var method in exportedMethods)
            {
                if (methodMap.TryGetValue(method.Name, out var existingMethod))
                {
                    throw new NotSupportedException(
                        Invariant($"React module '{GetType()}' with name '{Name}' has more than one ReactMethod with the name '{method.Name}'."));
                }

                methodMap.Add(method.Name, new NativeMethod(this, method));
            }

            return methodMap;
        }

        class NativeMethod : INativeMethod
        {
            private readonly Lazy<Action<IReactInstance, JArray>> _invokeDelegate;

            public NativeMethod(NativeModuleBase instance, MethodInfo method)
            {
                var delegateFactory = instance._delegateFactory;
                delegateFactory.Validate(method);
                _invokeDelegate = new Lazy<Action<IReactInstance, JArray>>(() => delegateFactory.Create(instance, method));
                Type = delegateFactory.GetMethodType(method);
            }

            public string Type
            {
                get;
            }

            public void Invoke(IReactInstance reactInstance, JArray jsArguments)
            {
                using (Tracer.Trace(Tracer.TRACE_TAG_REACT_BRIDGE, "callNativeModuleMethod").Start())
                {
                    _invokeDelegate.Value(reactInstance, jsArguments);
                }
            }
        }
    }
}
