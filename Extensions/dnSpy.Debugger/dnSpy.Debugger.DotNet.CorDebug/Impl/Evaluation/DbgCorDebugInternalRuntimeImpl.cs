﻿/*
    Copyright (C) 2014-2018 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using dndbg.COM.CorDebug;
using dndbg.Engine;
using dnlib.DotNet;
using dnlib.DotNet.Writer;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.CallStack;
using dnSpy.Contracts.Debugger.DotNet.CorDebug;
using dnSpy.Contracts.Debugger.DotNet.Evaluation;
using dnSpy.Contracts.Debugger.Engine.Evaluation;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Contracts.Metadata;
using dnSpy.Debugger.DotNet.CorDebug.CallStack;
using dnSpy.Debugger.DotNet.CorDebug.Impl.Evaluation.Hooks;
using dnSpy.Debugger.DotNet.Metadata;

namespace dnSpy.Debugger.DotNet.CorDebug.Impl.Evaluation {
	sealed class DbgCorDebugInternalRuntimeImpl : DbgCorDebugInternalRuntime, IDbgDotNetRuntime, ICorDebugRuntime {
		public override DbgRuntime Runtime { get; }
		public override DmdRuntime ReflectionRuntime { get; }
		public override CorDebugRuntimeVersion Version { get; }
		public override string ClrFilename { get; }
		public override string RuntimeDirectory { get; }
		public DbgDotNetDispatcher Dispatcher { get; }
		public DbgDotNetRuntimeFeatures Features => DbgDotNetRuntimeFeatures.ObjectIds;

		ICorDebugValueConverter ICorDebugRuntime.ValueConverter => corDebugValueConverter;

		readonly DbgEngineImpl engine;
		readonly Dictionary<DmdWellKnownType, ClassHook> classHooks;
		readonly ICorDebugValueConverter corDebugValueConverter;

		public DbgCorDebugInternalRuntimeImpl(DbgEngineImpl engine, DbgRuntime runtime, DmdRuntime reflectionRuntime, CorDebugRuntimeKind kind, string version, string clrPath, string runtimeDir) {
			this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
			Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			ReflectionRuntime = reflectionRuntime ?? throw new ArgumentNullException(nameof(reflectionRuntime));
			Version = new CorDebugRuntimeVersion(kind, version ?? throw new ArgumentNullException(nameof(version)));
			ClrFilename = clrPath ?? throw new ArgumentNullException(nameof(clrPath));
			RuntimeDirectory = runtimeDir ?? throw new ArgumentNullException(nameof(runtimeDir));
			Dispatcher = new DbgDotNetDispatcherImpl(engine);
			reflectionRuntime.GetOrCreateData(() => runtime);

			corDebugValueConverter = new CorDebugValueConverterImpl(this);
			classHooks = new Dictionary<DmdWellKnownType, ClassHook>();
			foreach (var info in ClassHookProvider.Create(this)) {
				Debug.Assert(info.Hook != null);
				Debug.Assert(!classHooks.ContainsKey(info.WellKnownType));
				classHooks.Add(info.WellKnownType, info.Hook);
			}
		}

		public ModuleId GetModuleId(DbgModule module) => engine.GetModuleId(module);

		public DbgDotNetRawModuleBytes GetRawModuleBytes(DbgModule module) {
			if (!module.IsDynamic)
				return DbgDotNetRawModuleBytes.None;
			if (Dispatcher.CheckAccess())
				return GetRawModuleBytesCore(module);
			return GetRawModuleBytesCore2(module);

			DbgDotNetRawModuleBytes GetRawModuleBytesCore2(DbgModule module2) =>
				Dispatcher.InvokeRethrow(() => GetRawModuleBytesCore(module2));
		}

		sealed class DynamicModuleMetadataState {
			public byte[] RawBytes;
			public ModuleDefMD Module;
			public int LoadClassVersion;
			public DbgDotNetRawModuleBytes ToDbgDotNetRawModuleBytes() {
				if (RawBytes != null)
					return new DbgDotNetRawModuleBytes(RawBytes, isFileLayout: true);
				return DbgDotNetRawModuleBytes.None;
			}
		}

		DbgDotNetRawModuleBytes GetRawModuleBytesCore(DbgModule module) {
			Dispatcher.VerifyAccess();
			if (!module.IsDynamic)
				return DbgDotNetRawModuleBytes.None;

			if (!engine.TryGetDnModuleAndVersion(module, out var dnModule, out int loadClassVersion))
				return DbgDotNetRawModuleBytes.None;

			var state = module.GetOrCreateData<DynamicModuleMetadataState>();
			if (state.RawBytes != null && state.LoadClassVersion == loadClassVersion)
				return state.ToDbgDotNetRawModuleBytes();

			var md = dnModule.GetOrCreateCorModuleDef();
			try {
				md.DisableMDAPICalls = false;
				md.LoadEverything(null);
			}
			finally {
				md.DisableMDAPICalls = true;
			}

			var resultStream = new MemoryStream();
			var options = new ModuleWriterOptions(md);
			options.MetadataOptions.Flags = MetadataFlags.PreserveRids;
			md.Write(resultStream, options);

			state.Module = null;
			state.RawBytes = resultStream.ToArray();
			state.LoadClassVersion = loadClassVersion;

			engine.RaiseModulesRefreshed(module);

			return state.ToDbgDotNetRawModuleBytes();
		}

		public bool TryGetMethodToken(DbgModule module, int methodToken, out int metadataMethodToken, out int metadataLocalVarSigTok) {
			if (!module.IsDynamic) {
				metadataMethodToken = 0;
				metadataLocalVarSigTok = 0;
				return false;
			}

			if (Dispatcher.CheckAccess())
				return TryGetMethodTokenCore(module, methodToken, out metadataMethodToken, out metadataLocalVarSigTok);
			return TryGetMethodTokenCore2(module, methodToken, out metadataMethodToken, out metadataLocalVarSigTok);

			bool TryGetMethodTokenCore2(DbgModule module2, int methodToken2, out int metadataMethodToken2, out int metadataLocalVarSigTok2) {
				int tmpMetadataMethodToken = 0, tmpMetadataLocalVarSigTok = 0;
				var res2 = Dispatcher.InvokeRethrow(() => {
					var res = TryGetMethodTokenCore(module2, methodToken2, out var metadataMethodToken3, out var metadataLocalVarSigTok3);
					tmpMetadataMethodToken = metadataMethodToken3;
					tmpMetadataLocalVarSigTok = metadataLocalVarSigTok3;
					return res;
				});
				metadataMethodToken2 = tmpMetadataMethodToken;
				metadataLocalVarSigTok2 = tmpMetadataLocalVarSigTok;
				return res2;
			}
		}

		bool TryGetMethodTokenCore(DbgModule module, int methodToken, out int metadataMethodToken, out int metadataLocalVarSigTok) {
			Dispatcher.VerifyAccess();
			DynamicModuleMetadataState state = null;
			if (module.IsDynamic && !module.TryGetData<DynamicModuleMetadataState>(out state)) {
				GetRawModuleBytesCore(module);
				bool b = module.TryGetData<DynamicModuleMetadataState>(out state);
				Debug.Assert(b);
			}
			if (state != null) {
				if (state.Module == null)
					state.Module = ModuleDefMD.Load(state.RawBytes);
				var method = state.Module.ResolveToken(methodToken) as MethodDef;
				if (method != null) {
					metadataMethodToken = method.MDToken.ToInt32();
					metadataLocalVarSigTok = (int)(method.Body?.LocalVarSigTok ?? 0);
					return true;
				}
			}

			metadataMethodToken = 0;
			metadataLocalVarSigTok = 0;
			return false;
		}

		sealed class GetFrameMethodState {
			public bool Initialized;
			public DmdMethodBase Method;
		}

		public DmdMethodBase GetFrameMethod(DbgEvaluationInfo evalInfo) {
			if (Dispatcher.CheckAccess())
				return GetFrameMethodCore(evalInfo);
			return GetFrameMethod2(evalInfo);

			DmdMethodBase GetFrameMethod2(DbgEvaluationInfo evalInfo2) =>
				Dispatcher.InvokeRethrow(() => GetFrameMethodCore(evalInfo2));
		}

		DmdMethodBase GetFrameMethodCore(DbgEvaluationInfo evalInfo) {
			Dispatcher.VerifyAccess();
			var state = evalInfo.Frame.GetOrCreateData<GetFrameMethodState>();
			if (!state.Initialized) {
				evalInfo.CancellationToken.ThrowIfCancellationRequested();
				if (ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame)) {
					ilFrame.GetFrameMethodInfo(out var module, out var methodMetadataToken, out var genericTypeArguments, out var genericMethodArguments);
					// Don't throw if it fails to resolve. Callers must be able to handle null return values
					var method = module?.ResolveMethod(methodMetadataToken, (IList<DmdType>)null, null, DmdResolveOptions.None);
					if ((object)method != null) {
						if (genericTypeArguments.Count != 0) {
							var type = method.ReflectedType.MakeGenericType(genericTypeArguments);
							method = type.GetMethod(method.Module, method.MetadataToken, throwOnError: true);
						}
						if (genericMethodArguments.Count != 0)
							method = ((DmdMethodInfo)method).MakeGenericMethod(genericMethodArguments);
					}
					state.Method = method;
				}
				state.Initialized = true;
			}
			return state.Method;
		}

		CorType GetType(CorAppDomain appDomain, DmdType type) => CorDebugTypeCreator.GetType(engine, appDomain, type);

		internal static CorValue TryGetObjectOrPrimitiveValue(CorValue value, out int hr) {
			hr = -1;
			if (value == null)
				return null;
			if (value.IsReference) {
				if (value.IsNull)
					throw new InvalidOperationException();
				value = value.GetDereferencedValue(out hr);
				if (value == null)
					return null;
			}
			if (value.IsBox) {
				value = value.GetBoxedValue(out hr);
				if (value == null)
					return null;
			}
			hr = 0;
			return value;
		}

		public DbgDotNetValue LoadFieldAddress(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdFieldInfo field) => null;

		public DbgDotNetValueResult LoadField(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdFieldInfo field) {
			if (Dispatcher.CheckAccess())
				return LoadFieldCore(evalInfo, obj, field);
			return LoadField2(evalInfo, obj, field);

			DbgDotNetValueResult LoadField2(DbgEvaluationInfo evalInfo2, DbgDotNetValue obj2, DmdFieldInfo field2) =>
				Dispatcher.InvokeRethrow(() => LoadFieldCore(evalInfo2, obj2, field2));
		}

		DbgDotNetValueResult LoadFieldCore(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdFieldInfo field) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
				var appDomain = ilFrame.GetCorAppDomain();

				int hr;
				CorType corFieldDeclType;
				CorValue fieldValue;
				var fieldDeclType = field.DeclaringType;
				if (obj == null) {
					if (!field.IsStatic)
						return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

					if (field.IsLiteral)
						return CreateSyntheticValue(field.FieldType, field.GetRawConstantValue());
					else {
						corFieldDeclType = GetType(appDomain, fieldDeclType);

						InitializeStaticConstructor(evalInfo, ilFrame, fieldDeclType, corFieldDeclType);
						fieldValue = corFieldDeclType.GetStaticFieldValue((uint)field.MetadataToken, ilFrame.CorFrame, out hr);
						if (fieldValue == null) {
							if (hr == CordbgErrors.CORDBG_E_CLASS_NOT_LOADED || hr == CordbgErrors.CORDBG_E_STATIC_VAR_NOT_AVAILABLE) {
								//TODO: Create a synthetic value init'd to the default value (0s or null ref)
							}
						}
						if (fieldValue == null)
							return DbgDotNetValueResult.CreateError(CordbgErrorHelper.GetErrorMessage(hr));
						return DbgDotNetValueResult.Create(engine.CreateDotNetValue_CorDebug(fieldValue, field.AppDomain, tryCreateStrongHandle: true));
					}
				}
				else {
					if (field.IsStatic)
						return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

					var objImp = obj as DbgDotNetValueImpl ?? throw new InvalidOperationException();
					corFieldDeclType = GetType(appDomain, fieldDeclType);
					var objValue = TryGetObjectOrPrimitiveValue(objImp.TryGetCorValue(), out hr);
					if (objValue == null)
						return DbgDotNetValueResult.CreateError(CordbgErrorHelper.GetErrorMessage(hr));
					if (objValue.IsObject) {
						fieldValue = objValue.GetFieldValue(corFieldDeclType.Class, (uint)field.MetadataToken, out hr);
						if (fieldValue == null)
							return DbgDotNetValueResult.CreateError(CordbgErrorHelper.GetErrorMessage(hr));
						return DbgDotNetValueResult.Create(engine.CreateDotNetValue_CorDebug(fieldValue, field.AppDomain, tryCreateStrongHandle: true));
					}
					else {
						if (IsPrimitiveValueType(objValue.ElementType)) {
							//TODO:
						}
					}
				}

				return DbgDotNetValueResult.CreateError("NYI");//TODO:
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public string StoreField(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdFieldInfo field, object value) {
			if (Dispatcher.CheckAccess())
				return StoreFieldCore(evalInfo, obj, field, value);
			return StoreField2(evalInfo, obj, field, value);

			string StoreField2(DbgEvaluationInfo evalInfo2, DbgDotNetValue obj2, DmdFieldInfo field2, object value2) =>
				Dispatcher.InvokeRethrow(() => StoreFieldCore(evalInfo2, obj2, field2, value2));
		}

		string StoreFieldCore(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdFieldInfo field, object value) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return CordbgErrorHelper.InternalError;
				var appDomain = ilFrame.GetCorAppDomain();

				CorType corFieldDeclType;
				var fieldDeclType = field.DeclaringType;
				if (obj == null) {
					if (!field.IsStatic)
						return CordbgErrorHelper.InternalError;

					if (field.IsLiteral)
						return CordbgErrorHelper.InternalError;
					else {
						corFieldDeclType = GetType(appDomain, fieldDeclType);

						InitializeStaticConstructor(evalInfo, ilFrame, fieldDeclType, corFieldDeclType);
						Func<CreateCorValueResult> createTargetValue = () => {
							var fieldValue = corFieldDeclType.GetStaticFieldValue((uint)field.MetadataToken, ilFrame.CorFrame, out var hr);
							return new CreateCorValueResult(fieldValue, hr);
						};
						return engine.StoreValue_CorDebug(evalInfo, ilFrame, createTargetValue, field.FieldType, value);
					}
				}
				else {
					if (field.IsStatic)
						return CordbgErrorHelper.InternalError;

					var objImp = obj as DbgDotNetValueImpl ?? throw new InvalidOperationException();
					corFieldDeclType = GetType(appDomain, fieldDeclType);
					var objValue = TryGetObjectOrPrimitiveValue(objImp.TryGetCorValue(), out int hr);
					if (objValue == null)
						return CordbgErrorHelper.GetErrorMessage(hr);
					if (objValue.IsObject) {
						Func<CreateCorValueResult> createTargetValue = () => {
							// Re-read it since it could've gotten neutered
							var objValue2 = TryGetObjectOrPrimitiveValue(objImp.TryGetCorValue(), out int hr2);
							Debug.Assert(objValue2?.IsObject == true);
							if (objValue2 == null)
								return new CreateCorValueResult(null, hr2);
							var fieldValue = objValue2.GetFieldValue(corFieldDeclType.Class, (uint)field.MetadataToken, out hr2);
							return new CreateCorValueResult(fieldValue, hr2);
						};
						return engine.StoreValue_CorDebug(evalInfo, ilFrame, createTargetValue, field.FieldType, value);
					}
					else {
						if (IsPrimitiveValueType(objValue.ElementType)) {
							//TODO:
						}
					}
				}

				return "NYI";//TODO:
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return CordbgErrorHelper.InternalError;
			}
		}

		static DbgDotNetValueResult CreateSyntheticValue(DmdType type, object constant) {
			var dnValue = SyntheticValueFactory.TryCreateSyntheticValue(type, constant);
			if (dnValue != null)
				return DbgDotNetValueResult.Create(dnValue);
			return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
		}

		sealed class StaticConstructorInitializedState {
			public volatile int Initialized;
		}

		void InitializeStaticConstructor(DbgEvaluationInfo evalInfo, ILDbgEngineStackFrame ilFrame, DmdType type, CorType corType) {
			if (engine.CheckFuncEval(evalInfo) != null)
				return;
			var state = type.GetOrCreateData<StaticConstructorInitializedState>();
			if (state.Initialized > 0 || Interlocked.Exchange(ref state.Initialized, 1) != 0)
				return;
			var cctor = type.TypeInitializer;
			if ((object)cctor != null) {
				foreach (var field in type.DeclaredFields) {
					if (!field.IsStatic || field.IsLiteral)
						continue;

					var fieldValue = corType.GetStaticFieldValue((uint)field.MetadataToken, ilFrame.CorFrame, out int hr);
					if (hr == CordbgErrors.CORDBG_E_CLASS_NOT_LOADED || hr == CordbgErrors.CORDBG_E_STATIC_VAR_NOT_AVAILABLE)
						break;
					if (fieldValue != null) {
						try {
							if (fieldValue.IsNull)
								continue;
							if (field.FieldType.IsValueType) {
								var objValue = fieldValue.GetDereferencedValue(out hr)?.GetBoxedValue(out hr);
								var data = objValue?.ReadGenericValue();
								if (data != null && !IsZero(data))
									return;
							}
							else {
								// It's a reference type and not null, so the field has been initialized
								return;
							}
						}
						finally {
							engine.DisposeHandle_CorDebug(fieldValue);
						}
					}
				}

				if (HasNativeCode(cctor))
					return;
			}

			DbgDotNetValueResult res = default;
			try {
				res = engine.FuncEvalCreateInstanceNoCtor_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), type);
				if (res.Value == null || res.ValueIsException)
					return;
				RuntimeHelpersRunClassConstructor(evalInfo, ilFrame, type, res.Value);
			}
			finally {
				res.Value?.Dispose();
			}
		}

		bool HasNativeCode(DmdMethodBase method) {
			var reflectionAppDomain = method.AppDomain;
			var methodDbgModule = method.Module.GetDebuggerModule() ?? throw new InvalidOperationException();
			if (!engine.TryGetDnModule(methodDbgModule, out var methodModule))
				return false;
			var func = methodModule.CorModule.GetFunctionFromToken((uint)method.MetadataToken) ?? throw new InvalidOperationException();
			return func.NativeCode != null;
		}

		// Calls System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor():
		//		RuntimeHelpers.RunClassConstructor(obj.GetType().TypeHandle);
		bool RuntimeHelpersRunClassConstructor(DbgEvaluationInfo evalInfo, ILDbgEngineStackFrame ilFrame, DmdType type, DbgDotNetValue objValue) {
			DbgDotNetValueResult getTypeRes = default;
			DbgDotNetValueResult typeHandleRes = default;
			DbgDotNetValueResult res = default;
			try {
				var reflectionAppDomain = type.AppDomain;
				var getTypeMethod = objValue.Type.GetMethod(nameof(object.GetType), DmdSignatureCallingConvention.Default | DmdSignatureCallingConvention.HasThis, 0, reflectionAppDomain.System_Type, Array.Empty<DmdType>(), throwOnError: false);
				Debug.Assert((object)getTypeMethod != null);
				if ((object)getTypeMethod == null)
					return false;
				var corAppDomain = ilFrame.GetCorAppDomain();
				getTypeRes = engine.FuncEvalCall_CorDebug(evalInfo, corAppDomain, getTypeMethod, objValue, Array.Empty<object>(), false);
				if (getTypeRes.Value == null || getTypeRes.ValueIsException)
					return false;
				var typeObj = getTypeRes.Value;
				var runtimeTypeHandleType = reflectionAppDomain.GetWellKnownType(DmdWellKnownType.System_RuntimeTypeHandle, isOptional: true);
				Debug.Assert((object)runtimeTypeHandleType != null);
				if ((object)runtimeTypeHandleType == null)
					return false;
				var getTypeHandleMethod = typeObj.Type.GetMethod("get_" + nameof(Type.TypeHandle), DmdSignatureCallingConvention.Default | DmdSignatureCallingConvention.HasThis, 0, runtimeTypeHandleType, Array.Empty<DmdType>(), throwOnError: false);
				Debug.Assert((object)getTypeHandleMethod != null);
				if ((object)getTypeHandleMethod == null)
					return false;
				typeHandleRes = engine.FuncEvalCall_CorDebug(evalInfo, corAppDomain, getTypeHandleMethod, typeObj, Array.Empty<object>(), false);
				if (typeHandleRes.Value == null || typeHandleRes.ValueIsException)
					return false;
				var runtimeHelpersType = reflectionAppDomain.GetWellKnownType(DmdWellKnownType.System_Runtime_CompilerServices_RuntimeHelpers, isOptional: true);
				var runClassConstructorMethod = runtimeHelpersType?.GetMethod(nameof(RuntimeHelpers.RunClassConstructor), DmdSignatureCallingConvention.Default, 0, reflectionAppDomain.System_Void, new[] { runtimeTypeHandleType }, throwOnError: false);
				Debug.Assert((object)runClassConstructorMethod != null);
				if ((object)runClassConstructorMethod == null)
					return false;
				res = engine.FuncEvalCall_CorDebug(evalInfo, corAppDomain, runClassConstructorMethod, null, new[] { typeHandleRes.Value }, false);
				return !res.HasError && !res.ValueIsException;
			}
			finally {
				getTypeRes.Value?.Dispose();
				typeHandleRes.Value?.Dispose();
				res.Value?.Dispose();
			}
		}

		static bool IsZero(byte[] a) {
			for (int i = 0; i < a.Length; i++) {
				if (a[i] != 0)
					return false;
			}
			return true;
		}

		static bool IsPrimitiveValueType(CorElementType etype) {
			switch (etype) {
			case CorElementType.Boolean:
			case CorElementType.Char:
			case CorElementType.I1:
			case CorElementType.U1:
			case CorElementType.I2:
			case CorElementType.U2:
			case CorElementType.I4:
			case CorElementType.U4:
			case CorElementType.I8:
			case CorElementType.U8:
			case CorElementType.R4:
			case CorElementType.R8:
			case CorElementType.I:
			case CorElementType.U:
				return true;

			default:
				return false;
			}
		}

		public DbgDotNetValueResult Call(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdMethodBase method, object[] arguments, DbgDotNetInvokeOptions invokeOptions) {
			if (Dispatcher.CheckAccess())
				return CallCore(evalInfo, obj, method, arguments, invokeOptions);
			return Call2(evalInfo, obj, method, arguments, invokeOptions);

			DbgDotNetValueResult Call2(DbgEvaluationInfo evalInfo2, DbgDotNetValue obj2, DmdMethodBase method2, object[] arguments2, DbgDotNetInvokeOptions invokeOptions2) =>
				Dispatcher.InvokeRethrow(() => CallCore(evalInfo2, obj2, method2, arguments2, invokeOptions2));
		}

		DbgDotNetValueResult CallCore(DbgEvaluationInfo evalInfo, DbgDotNetValue obj, DmdMethodBase method, object[] arguments, DbgDotNetInvokeOptions invokeOptions) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

				var type = method.DeclaringType;
				if (type.IsConstructedGenericType)
					type = type.GetGenericTypeDefinition();
				var typeName = DmdTypeName.Create(type);
				if (DmdWellKnownTypeUtils.TryGetWellKnownType(typeName, out var wellKnownType)) {
					if (classHooks.TryGetValue(wellKnownType, out var hook) && type == type.AppDomain.GetWellKnownType(wellKnownType, isOptional: true)) {
						var res = hook.Call(obj, method, arguments);
						if (res != null)
							return DbgDotNetValueResult.Create(res);
					}
				}

				return engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), method, obj, arguments, newObj: false);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public DbgDotNetValueResult CreateInstance(DbgEvaluationInfo evalInfo, DmdConstructorInfo ctor, object[] arguments, DbgDotNetInvokeOptions invokeOptions) {
			if (Dispatcher.CheckAccess())
				return CreateInstanceCore(evalInfo, ctor, arguments, invokeOptions);
			return CreateInstance2(evalInfo, ctor, arguments, invokeOptions);

			DbgDotNetValueResult CreateInstance2(DbgEvaluationInfo evalInfo2, DmdConstructorInfo ctor2, object[] arguments2, DbgDotNetInvokeOptions invokeOptions2) =>
				Dispatcher.InvokeRethrow(() => CreateInstanceCore(evalInfo2, ctor2, arguments2, invokeOptions2));
		}

		DbgDotNetValueResult CreateInstanceCore(DbgEvaluationInfo evalInfo, DmdConstructorInfo ctor, object[] arguments, DbgDotNetInvokeOptions invokeOptions) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

				var type = ctor.DeclaringType;
				if (type.IsConstructedGenericType)
					type = type.GetGenericTypeDefinition();
				var typeName = DmdTypeName.Create(type);
				if (DmdWellKnownTypeUtils.TryGetWellKnownType(typeName, out var wellKnownType)) {
					if (classHooks.TryGetValue(wellKnownType, out var hook) && type == type.AppDomain.GetWellKnownType(wellKnownType, isOptional: true)) {
						var res = hook.CreateInstance(ctor, arguments);
						if (res != null)
							return DbgDotNetValueResult.Create(res);
					}
				}

				return engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), ctor, null, arguments, newObj: true);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public DbgDotNetValueResult CreateInstanceNoConstructor(DbgEvaluationInfo evalInfo, DmdType type) {
			if (Dispatcher.CheckAccess())
				return CreateInstanceNoConstructorCore(evalInfo, type);
			return CreateInstanceNoConstructor2(evalInfo, type);

			DbgDotNetValueResult CreateInstanceNoConstructor2(DbgEvaluationInfo evalInfo2, DmdType type2) =>
				Dispatcher.InvokeRethrow(() => CreateInstanceNoConstructorCore(evalInfo2, type2));
		}

		DbgDotNetValueResult CreateInstanceNoConstructorCore(DbgEvaluationInfo evalInfo, DmdType type) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
				return engine.FuncEvalCreateInstanceNoCtor_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), type);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public DbgDotNetValueResult CreateSZArray(DbgEvaluationInfo evalInfo, DmdType elementType, int length) {
			if (Dispatcher.CheckAccess())
				return CreateSZArrayCore(evalInfo, elementType, length);
			return CreateSZArray2(evalInfo, elementType, length);

			DbgDotNetValueResult CreateSZArray2(DbgEvaluationInfo evalInfo2, DmdType elementType2, int length2) =>
				Dispatcher.InvokeRethrow(() => CreateSZArrayCore(evalInfo2, elementType2, length2));
		}

		DbgDotNetValueResult CreateSZArrayCore(DbgEvaluationInfo evalInfo, DmdType elementType, int length) {
			Dispatcher.VerifyAccess();
			try {
				if (!CanCallNewParameterizedArray(elementType))
					return CreateSZArrayCore_Array_CreateInstance(evalInfo, elementType, length);
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
				return engine.CreateSZArray_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), elementType, length);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		DbgDotNetValueResult CreateSZArrayCore_Array_CreateInstance(DbgEvaluationInfo evalInfo, DmdType elementType, int length) {
			Dispatcher.VerifyAccess();
			Debug.Assert(!CanCallNewParameterizedArray(elementType));
			if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

			// Execute this code:
			//	var elementType = Type.GetType(elementType.AssemblyQualifiedName);
			//	return Array.CreateInstance(elementType, length);

			var appDomain = elementType.AppDomain;
			DbgDotNetValue typeElementType = null;
			try {
				var methodGetType = appDomain.System_Type.GetMethod(nameof(Type.GetType), DmdSignatureCallingConvention.Default, 0, appDomain.System_Type, new[] { appDomain.System_String }, throwOnError: true);
				var res = engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), methodGetType, null, new[] { elementType.AssemblyQualifiedName }, false);
				if (res.HasError || res.ValueIsException)
					return res;
				typeElementType = res.Value;
				if (res.Value.IsNull)
					return DbgDotNetValueResult.CreateError(PredefinedEvaluationErrorMessages.InternalDebuggerError);

				var methodCreateInstance = appDomain.System_Array.GetMethod(nameof(Array.CreateInstance), DmdSignatureCallingConvention.Default, 0, appDomain.System_Array, new[] { appDomain.System_Type, appDomain.System_Int32 }, throwOnError: true);
				return engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), methodCreateInstance, null, new object[] { typeElementType, length }, false);
			}
			finally {
				typeElementType?.Dispose();
			}
		}

		// ICorDebugEval2.NewParameterizedArray() can only create arrays of reference types or
		// of primitive value types, but not including IntPtr/UIntPtr, see coreclr code, funceval.cpp, case DB_IPCE_FET_NEW_ARRAY:
		//		// Gotta be a primitive, class, or System.Object.
		//		if (((et < ELEMENT_TYPE_BOOLEAN) || (et > ELEMENT_TYPE_R8)) &&
		//			!IsElementTypeSpecial(et)) // <-- Class,Object,Array,SZArray,String
		//		{
		//			COMPlusThrow(kArgumentOutOfRangeException, W("ArgumentOutOfRange_Enum"));
		static bool CanCallNewParameterizedArray(DmdType elementType) {
			switch (elementType.TypeSignatureKind) {
			case DmdTypeSignatureKind.SZArray:
			case DmdTypeSignatureKind.MDArray:
				return true;

			case DmdTypeSignatureKind.Pointer:
			case DmdTypeSignatureKind.ByRef:
			case DmdTypeSignatureKind.TypeGenericParameter:
			case DmdTypeSignatureKind.MethodGenericParameter:
			case DmdTypeSignatureKind.FunctionPointer:
				return false;

			case DmdTypeSignatureKind.Type:
			case DmdTypeSignatureKind.GenericInstance:
				if (!elementType.IsValueType)
					return true;
				if (elementType.IsEnum)
					return false;

				var tc = DmdType.GetTypeCode(elementType);
				if (TypeCode.Boolean <= tc && tc <= TypeCode.Double)
					return true;

				return false;

			default:
				throw new InvalidOperationException();
			}
		}

		public DbgDotNetValueResult CreateArray(DbgEvaluationInfo evalInfo, DmdType elementType, DbgDotNetArrayDimensionInfo[] dimensionInfos) {
			if (Dispatcher.CheckAccess())
				return CreateArrayCore(evalInfo, elementType, dimensionInfos);
			return CreateArray2(evalInfo, elementType, dimensionInfos);

			DbgDotNetValueResult CreateArray2(DbgEvaluationInfo evalInfo2, DmdType elementType2, DbgDotNetArrayDimensionInfo[] dimensionInfos2) =>
				Dispatcher.InvokeRethrow(() => CreateArrayCore(evalInfo2, elementType2, dimensionInfos2));
		}

		DbgDotNetValueResult CreateArrayCore(DbgEvaluationInfo evalInfo, DmdType elementType, DbgDotNetArrayDimensionInfo[] dimensionInfos) {
			Dispatcher.VerifyAccess();
			if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);

			// There's no ICorDebugEval method that can create multi-dimensional arrays so
			// we have to use Array.CreateInstance(Type, int[], int[]).

			var appDomain = elementType.AppDomain;
			DbgDotNetValue typeElementType = null;
			try {
				var methodGetType = appDomain.System_Type.GetMethod(nameof(Type.GetType), DmdSignatureCallingConvention.Default, 0, appDomain.System_Type, new[] { appDomain.System_String }, throwOnError: true);
				var res = engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), methodGetType, null, new[] { elementType.AssemblyQualifiedName }, false);
				if (res.HasError || res.ValueIsException)
					return res;
				typeElementType = res.Value;
				if (res.Value.IsNull)
					return DbgDotNetValueResult.CreateError(PredefinedEvaluationErrorMessages.InternalDebuggerError);

				var lengths = new int[dimensionInfos.Length];
				var lowerBounds = new int[dimensionInfos.Length];
				for (int i = 0; i < dimensionInfos.Length; i++) {
					lengths[i] = (int)dimensionInfos[i].Length;
					lowerBounds[i] = dimensionInfos[i].BaseIndex;
				}

				var methodCreateInstance = appDomain.System_Array.GetMethod(nameof(Array.CreateInstance), DmdSignatureCallingConvention.Default, 0, appDomain.System_Array, new[] { appDomain.System_Type, appDomain.System_Int32.MakeArrayType(), appDomain.System_Int32.MakeArrayType() }, throwOnError: true);
				res = engine.FuncEvalCall_CorDebug(evalInfo, ilFrame.GetCorAppDomain(), methodCreateInstance, null, new object[] { typeElementType, lengths, lowerBounds }, false);
				if (res.HasError || res.ValueIsException)
					return res;

				return res;
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
			finally {
				typeElementType?.Dispose();
			}
		}

		public DbgDotNetAliasInfo[] GetAliases(DbgEvaluationInfo evalInfo) {
			if (Dispatcher.CheckAccess())
				return GetAliasesCore(evalInfo);
			return GetAliases2(evalInfo);

			DbgDotNetAliasInfo[] GetAliases2(DbgEvaluationInfo evalInfo2) =>
				Dispatcher.InvokeRethrow(() => GetAliasesCore(evalInfo2));
		}

		DbgDotNetAliasInfo[] GetAliasesCore(DbgEvaluationInfo evalInfo) {
			Dispatcher.VerifyAccess();

			DbgDotNetValue exception = null;
			DbgDotNetValue stowedException = null;
			var returnValues = Array.Empty<DbgDotNetReturnValueInfo>();
			try {
				exception = GetExceptionCore(evalInfo, DbgDotNetRuntimeConstants.ExceptionId);
				stowedException = GetStowedExceptionCore(evalInfo, DbgDotNetRuntimeConstants.StowedExceptionId);
				returnValues = GetReturnValuesCore(evalInfo);

				int count = (exception != null ? 1 : 0) + (stowedException != null ? 1 : 0) + returnValues.Length + (returnValues.Length != 0 ? 1 : 0);
				if (count == 0)
					return Array.Empty<DbgDotNetAliasInfo>();

				var res = new DbgDotNetAliasInfo[count];
				int w = 0;
				if (exception != null)
					res[w++] = new DbgDotNetAliasInfo(DbgDotNetAliasInfoKind.Exception, exception.Type, DbgDotNetRuntimeConstants.ExceptionId, Guid.Empty, null);
				if (stowedException != null)
					res[w++] = new DbgDotNetAliasInfo(DbgDotNetAliasInfoKind.StowedException, stowedException.Type, DbgDotNetRuntimeConstants.StowedExceptionId, Guid.Empty, null);
				if (returnValues.Length != 0) {
					res[w++] = new DbgDotNetAliasInfo(DbgDotNetAliasInfoKind.ReturnValue, returnValues[returnValues.Length - 1].Value.Type, DbgDotNetRuntimeConstants.LastReturnValueId, Guid.Empty, null);
					foreach (var returnValue in returnValues) {
						Debug.Assert(returnValue.Id != DbgDotNetRuntimeConstants.LastReturnValueId);
						res[w++] = new DbgDotNetAliasInfo(DbgDotNetAliasInfoKind.ReturnValue, returnValue.Value.Type, returnValue.Id, Guid.Empty, null);
					}
				}
				if (w != res.Length)
					throw new InvalidOperationException();
				return res;
			}
			finally {
				exception?.Dispose();
				stowedException?.Dispose();
				foreach (var rv in returnValues)
					rv.Value?.Dispose();
			}
		}

		public DbgDotNetExceptionInfo[] GetExceptions(DbgEvaluationInfo evalInfo) {
			if (Dispatcher.CheckAccess())
				return GetExceptionsCore(evalInfo);
			return GetExceptions2(evalInfo);

			DbgDotNetExceptionInfo[] GetExceptions2(DbgEvaluationInfo evalInfo2) =>
				Dispatcher.InvokeRethrow(() => GetExceptionsCore(evalInfo2));
		}

		DbgDotNetExceptionInfo[] GetExceptionsCore(DbgEvaluationInfo evalInfo) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			DbgDotNetValue exception = null;
			DbgDotNetValue stowedException = null;
			try {
				exception = GetExceptionCore(evalInfo, DbgDotNetRuntimeConstants.ExceptionId);
				stowedException = GetStowedExceptionCore(evalInfo, DbgDotNetRuntimeConstants.StowedExceptionId);
				int count = (exception != null ? 1 : 0) + (stowedException != null ? 1 : 0);
				if (count == 0)
					return Array.Empty<DbgDotNetExceptionInfo>();
				var res = new DbgDotNetExceptionInfo[count];
				int w = 0;
				if (exception != null)
					res[w++] = new DbgDotNetExceptionInfo(exception, DbgDotNetRuntimeConstants.ExceptionId, DbgDotNetExceptionInfoFlags.None);
				if (stowedException != null)
					res[w++] = new DbgDotNetExceptionInfo(stowedException, DbgDotNetRuntimeConstants.StowedExceptionId, DbgDotNetExceptionInfoFlags.StowedException);
				if (w != res.Length)
					throw new InvalidOperationException();
				return res;
			}
			catch {
				exception?.Dispose();
				stowedException?.Dispose();
				throw;
			}
		}

		public DbgDotNetReturnValueInfo[] GetReturnValues(DbgEvaluationInfo evalInfo) {
			if (Dispatcher.CheckAccess())
				return GetReturnValuesCore(evalInfo);
			return GetReturnValues2(evalInfo);

			DbgDotNetReturnValueInfo[] GetReturnValues2(DbgEvaluationInfo evalInfo2) =>
				Dispatcher.InvokeRethrow(() => GetReturnValuesCore(evalInfo2));
		}

		DbgDotNetReturnValueInfo[] GetReturnValuesCore(DbgEvaluationInfo evalInfo) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			return engine.GetCurrentReturnValues();
		}

		public DbgDotNetValue GetException(DbgEvaluationInfo evalInfo, uint id) {
			if (Dispatcher.CheckAccess())
				return GetExceptionCore(evalInfo, id);
			return GetException2(evalInfo, id);

			DbgDotNetValue GetException2(DbgEvaluationInfo evalInfo2, uint id2) =>
				Dispatcher.InvokeRethrow(() => GetExceptionCore(evalInfo2, id2));
		}

		DbgDotNetValue GetExceptionCore(DbgEvaluationInfo evalInfo, uint id) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			if (id != DbgDotNetRuntimeConstants.ExceptionId)
				return null;
			var corException = TryGetException(evalInfo.Frame);
			if (corException == null)
				return null;
			var reflectionAppDomain = evalInfo.Frame.AppDomain.GetReflectionAppDomain() ?? throw new InvalidOperationException();
			return engine.CreateDotNetValue_CorDebug(corException, reflectionAppDomain, tryCreateStrongHandle: true);
		}

		public DbgDotNetValue GetStowedException(DbgEvaluationInfo evalInfo, uint id) {
			if (Dispatcher.CheckAccess())
				return GetStowedExceptionCore(evalInfo, id);
			return GetStowedException2(evalInfo, id);

			DbgDotNetValue GetStowedException2(DbgEvaluationInfo evalInfo2, uint id2) =>
				Dispatcher.InvokeRethrow(() => GetStowedExceptionCore(evalInfo2, id2));
		}

		DbgDotNetValue GetStowedExceptionCore(DbgEvaluationInfo evalInfo, uint id) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			if (id != DbgDotNetRuntimeConstants.StowedExceptionId)
				return null;
			var corStowedException = TryGetStowedException(evalInfo.Frame);
			if (corStowedException == null)
				return null;
			var reflectionAppDomain = evalInfo.Frame.AppDomain.GetReflectionAppDomain() ?? throw new InvalidOperationException();
			return engine.CreateDotNetValue_CorDebug(corStowedException, reflectionAppDomain, tryCreateStrongHandle: true);
		}

		CorValue TryGetException(DbgStackFrame frame) {
			Dispatcher.VerifyAccess();
			var dnThread = engine.GetThread(frame.Thread);
			return dnThread.CorThread.CurrentException;
		}

		CorValue TryGetStowedException(DbgStackFrame frame) {
			Dispatcher.VerifyAccess();
			return null;//TODO:
		}

		public DbgDotNetValue GetReturnValue(DbgEvaluationInfo evalInfo, uint id) {
			if (Dispatcher.CheckAccess())
				return GetReturnValueCore(evalInfo, id);
			return GetReturnValue2(evalInfo, id);

			DbgDotNetValue GetReturnValue2(DbgEvaluationInfo evalInfo2, uint id2) =>
				Dispatcher.InvokeRethrow(() => GetReturnValueCore(evalInfo2, id2));
		}

		DbgDotNetValue GetReturnValueCore(DbgEvaluationInfo evalInfo, uint id) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			return engine.GetCurrentReturnValue(id);
		}

		DbgDotNetValueResult CreateValue(CorValue value, ILDbgEngineStackFrame ilFrame) {
			var reflectionAppDomain = ilFrame.GetReflectionModule().AppDomain;
			var dnValue = engine.CreateDotNetValue_CorDebug(value, reflectionAppDomain, tryCreateStrongHandle: true);
			return DbgDotNetValueResult.Create(dnValue);
		}

		public DbgDotNetValueResult GetLocalValue(DbgEvaluationInfo evalInfo, uint index) {
			if (Dispatcher.CheckAccess())
				return GetLocalValueCore(evalInfo, index);
			return GetLocalValue2(evalInfo, index);

			DbgDotNetValueResult GetLocalValue2(DbgEvaluationInfo evalInfo2, uint index2) =>
				Dispatcher.InvokeRethrow(() => GetLocalValueCore(evalInfo2, index2));
		}

		DbgDotNetValueResult GetLocalValueCore(DbgEvaluationInfo evalInfo, uint index) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					throw new InvalidOperationException();
				var value = ilFrame.CorFrame.GetILLocal(index, out int hr);
				if (value == null)
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.GetErrorMessage(hr));
				return CreateValue(value, ilFrame);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public DbgDotNetValueResult GetParameterValue(DbgEvaluationInfo evalInfo, uint index) {
			if (Dispatcher.CheckAccess())
				return GetParameterValueCore(evalInfo, index);
			return GetParameterValue2(evalInfo, index);

			DbgDotNetValueResult GetParameterValue2(DbgEvaluationInfo evalInfo2, uint index2) =>
				Dispatcher.InvokeRethrow(() => GetParameterValueCore(evalInfo2, index2));
		}

		DbgDotNetValueResult GetParameterValueCore(DbgEvaluationInfo evalInfo, uint index) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					throw new InvalidOperationException();
				var value = ilFrame.CorFrame.GetILArgument(index, out int hr);
				if (value == null)
					return DbgDotNetValueResult.CreateError(CordbgErrorHelper.GetErrorMessage(hr));
				return CreateValue(value, ilFrame);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public string SetLocalValue(DbgEvaluationInfo evalInfo, uint index, DmdType targetType, object value) {
			if (Dispatcher.CheckAccess())
				return SetLocalValueCore(evalInfo, index, targetType, value);
			return SetLocalValue2(evalInfo, index, targetType, value);

			string SetLocalValue2(DbgEvaluationInfo evalInfo2, uint index2, DmdType targetType2, object value2) =>
				Dispatcher.InvokeRethrow(() => SetLocalValueCore(evalInfo2, index2, targetType2, value2));
		}

		string SetLocalValueCore(DbgEvaluationInfo evalInfo, uint index, DmdType targetType, object value) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					throw new InvalidOperationException();
				return engine.SetLocalValue_CorDebug(evalInfo, ilFrame, index, targetType, value);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return CordbgErrorHelper.InternalError;
			}
		}

		public string SetParameterValue(DbgEvaluationInfo evalInfo, uint index, DmdType targetType, object value) {
			if (Dispatcher.CheckAccess())
				return SetParameterValueCore(evalInfo, index, targetType, value);
			return SetParameterValue2(evalInfo, index, targetType, value);

			string SetParameterValue2(DbgEvaluationInfo evalInfo2, uint index2, DmdType targetType2, object value2) =>
				Dispatcher.InvokeRethrow(() => SetParameterValueCore(evalInfo2, index2, targetType2, value2));
		}

		string SetParameterValueCore(DbgEvaluationInfo evalInfo, uint index, DmdType targetType, object value) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					throw new InvalidOperationException();
				return engine.SetParameterValue_CorDebug(evalInfo, ilFrame, index, targetType, value);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return CordbgErrorHelper.InternalError;
			}
		}

		public DbgDotNetValue GetLocalValueAddress(DbgEvaluationInfo evalInfo, uint index, DmdType targetType) => null;
		public DbgDotNetValue GetParameterValueAddress(DbgEvaluationInfo evalInfo, uint index, DmdType targetType) => null;

		public DbgDotNetValueResult CreateValue(DbgEvaluationInfo evalInfo, object value) {
			if (Dispatcher.CheckAccess())
				return CreateValueCore(evalInfo, value);
			return CreateValue2(evalInfo, value);

			DbgDotNetValueResult CreateValue2(DbgEvaluationInfo evalInfo2, object value2) =>
				Dispatcher.InvokeRethrow(() => CreateValueCore(evalInfo2, value2));
		}

		DbgDotNetValueResult CreateValueCore(DbgEvaluationInfo evalInfo, object value) {
			Dispatcher.VerifyAccess();
			try {
				if (!ILDbgEngineStackFrame.TryGetEngineStackFrame(evalInfo.Frame, out var ilFrame))
					throw new InvalidOperationException();
				return engine.CreateValue_CorDebug(evalInfo, ilFrame, value);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
		}

		public DbgDotNetValueResult Box(DbgEvaluationInfo evalInfo, object value) {
			if (Dispatcher.CheckAccess())
				return BoxCore(evalInfo, value);
			return Box2(evalInfo, value);

			DbgDotNetValueResult Box2(DbgEvaluationInfo evalInfo2, object value2) =>
				Dispatcher.InvokeRethrow(() => BoxCore(evalInfo2, value2));
		}

		DbgDotNetValueResult BoxCore(DbgEvaluationInfo evalInfo, object value) {
			Dispatcher.VerifyAccess();
			evalInfo.CancellationToken.ThrowIfCancellationRequested();
			DbgDotNetValueResult res = default;
			try {
				res = CreateValueCore(evalInfo, value);
				if (res.ErrorMessage != null)
					return res;
				var boxedValue = res.Value.Box(evalInfo);
				if (boxedValue != null)
					return boxedValue.Value;
				return DbgDotNetValueResult.CreateError(PredefinedEvaluationErrorMessages.InternalDebuggerError);
			}
			catch (Exception ex) when (ExceptionUtils.IsInternalDebuggerError(ex)) {
				return DbgDotNetValueResult.CreateError(CordbgErrorHelper.InternalError);
			}
			finally {
				res.Value?.Dispose();
			}
		}

		public bool CanCreateObjectId(DbgDotNetValue value) {
			var valueImpl = value as DbgDotNetValueImpl;
			if (valueImpl == null)
				return false;
			if (Dispatcher.CheckAccess())
				return CanCreateObjectIdCore(valueImpl);
			return CanCreateObjectId2(valueImpl);

			bool CanCreateObjectId2(DbgDotNetValueImpl value2) =>
				Dispatcher.InvokeRethrow(() => CanCreateObjectIdCore(value2));
		}

		bool CanCreateObjectIdCore(DbgDotNetValueImpl value) {
			Dispatcher.VerifyAccess();

			// Keep this in sync with CreateObjectIdCore()
			var corValue = value.TryGetCorValue();
			if (corValue == null)
				return false;
			if (corValue.IsNull)
				return false;
			if (!corValue.IsHandle) {
				if (corValue.IsReference) {
					if (corValue.IsNull)
						return false;
					corValue = corValue.GetDereferencedValue(out int hr);
					if (corValue == null)
						return false;
				}
				if (!corValue.IsHeap2)
					return false;
			}

			return true;
		}

		public DbgDotNetObjectId CreateObjectId(DbgDotNetValue value, uint id) {
			var valueImpl = value as DbgDotNetValueImpl;
			if (valueImpl == null)
				return null;
			if (Dispatcher.CheckAccess())
				return CreateObjectIdCore(valueImpl, id);
			return CreateObjectId2(valueImpl, id);

			DbgDotNetObjectId CreateObjectId2(DbgDotNetValueImpl value2, uint id2) =>
				Dispatcher.InvokeRethrow(() => CreateObjectIdCore(value2, id2));
		}

		DbgDotNetObjectId CreateObjectIdCore(DbgDotNetValueImpl value, uint id) {
			Dispatcher.VerifyAccess();

			// Keep this in sync with CanCreateObjectIdCore()
			var corValue = value.TryGetCorValue();
			if (corValue == null)
				return null;
			if (corValue.IsNull)
				return null;
			if (corValue.IsHandle) {
				var valueHolder = value.CorValueHolder.AddRef();
				try {
					return new DbgDotNetObjectIdImpl(valueHolder, id);
				}
				catch {
					valueHolder.Release();
					throw;
				}
			}
			else {
				if (corValue.IsReference) {
					if (corValue.IsNull)
						return null;
					corValue = corValue.GetDereferencedValue(out int hr);
					if (corValue == null)
						return null;
				}
				var strongHandle = corValue.CreateHandle(CorDebugHandleType.HANDLE_STRONG);
				if (strongHandle == null)
					return null;
				try {
					return new DbgDotNetObjectIdImpl(new DbgCorValueHolder(engine, strongHandle, value.Type), id);
				}
				catch {
					engine.DisposeHandle_CorDebug(strongHandle);
					throw;
				}
			}
		}

		public bool Equals(DbgDotNetObjectId objectId, DbgDotNetValue value) {
			var objectIdImpl = objectId as DbgDotNetObjectIdImpl;
			var valueImpl = value as DbgDotNetValueImpl;
			if (objectIdImpl == null || valueImpl == null)
				return false;
			if (Dispatcher.CheckAccess())
				return EqualsCore(objectIdImpl, valueImpl);
			return Equals2(objectIdImpl, valueImpl);

			bool Equals2(DbgDotNetObjectIdImpl objectId2, DbgDotNetValueImpl value2) =>
				Dispatcher.InvokeRethrow(() => EqualsCore(objectId2, value2));
		}

		readonly struct EquatableValue {
			public readonly ulong Address;
			readonly DmdType type;

			public EquatableValue(DmdType type, CorValue value) {
				if (value == null)
					Address = 0;
				else if (type.IsByRef)
					Address = value.ReferenceAddress;
				else {
					if (value.IsReference)
						value = value.GetDereferencedValue(out int hr);
					Address = value?.Address ?? 0;
				}
				this.type = type;
			}

			public bool Equals2(in EquatableValue other) => Address != 0 && Address == other.Address;
			public bool? Equals3(in EquatableValue other) => Address == 0 && other.Address == 0 ? (bool?)null : Address == other.Address;
			// Value must be stable, so we can't use Address (obj could get moved by the GC). It's used by dictionaries.
			public new int GetHashCode() => Address == 0 ? 0 : type.AssemblyQualifiedName.GetHashCode();
		}

		bool EqualsCore(DbgDotNetObjectIdImpl objectId, DbgDotNetValueImpl value) {
			Dispatcher.VerifyAccess();

			var idHolder = objectId.Value;
			var vHolder = value.CorValueHolder;
			if (idHolder == vHolder)
				return true;
			var v1 = GetEquatableValue(idHolder.Type, idHolder.CorValue);
			var v2 = GetEquatableValue(vHolder.Type, vHolder.CorValue);
			return v1.Equals2(v2);
		}

		static EquatableValue GetEquatableValue(DmdType type, CorValue corValue) => new EquatableValue(type, corValue);

		public int GetHashCode(DbgDotNetObjectId objectId) {
			var objectIdImpl = objectId as DbgDotNetObjectIdImpl;
			if (objectIdImpl == null)
				return 0;
			if (Dispatcher.CheckAccess())
				return GetHashCodeCore(objectIdImpl);
			return GetHashCode2(objectIdImpl);

			int GetHashCode2(DbgDotNetObjectIdImpl objectId2) =>
				Dispatcher.InvokeRethrow(() => GetHashCodeCore(objectId2));
		}

		int GetHashCodeCore(DbgDotNetObjectIdImpl objectId) {
			Dispatcher.VerifyAccess();
			return GetEquatableValue(objectId.Value.Type, objectId.Value.CorValue).GetHashCode();
		}

		public int GetHashCode(DbgDotNetValue value) {
			var valueImpl = value as DbgDotNetValueImpl;
			if (valueImpl == null)
				return 0;
			if (Dispatcher.CheckAccess())
				return GetHashCodeCore(valueImpl);
			return GetHashCode2(valueImpl);

			int GetHashCode2(DbgDotNetValueImpl value2) =>
				Dispatcher.InvokeRethrow(() => GetHashCodeCore(value2));
		}

		int GetHashCodeCore(DbgDotNetValueImpl value) {
			Dispatcher.VerifyAccess();
			return GetEquatableValue(value.Type, value.TryGetCorValue()).GetHashCode();
		}

		public DbgDotNetValue GetValue(DbgEvaluationInfo evalInfo, DbgDotNetObjectId objectId) {
			var objectIdImpl = objectId as DbgDotNetObjectIdImpl;
			if (objectIdImpl == null)
				throw new ArgumentException();
			if (Dispatcher.CheckAccess())
				return GetValueCore(evalInfo, objectIdImpl);
			return GetValue2(evalInfo, objectIdImpl);

			DbgDotNetValue GetValue2(DbgEvaluationInfo evalInfo2, DbgDotNetObjectIdImpl objectId2) =>
				Dispatcher.InvokeRethrow(() => GetValueCore(evalInfo2, objectId2));
		}

		DbgDotNetValue GetValueCore(DbgEvaluationInfo evalInfo, DbgDotNetObjectIdImpl objectId) {
			Dispatcher.VerifyAccess();
			var dnValue = objectId.Value.AddRef();
			try {
				return engine.CreateDotNetValue_CorDebug(dnValue);
			}
			catch {
				dnValue.Release();
				throw;
			}
		}

		public bool? Equals(DbgDotNetValue a, DbgDotNetValue b) {
			if (a == b)
				return true;
			if (a.Type != b.Type)
				return false;
			var ai = a as DbgDotNetValueImpl;
			var bi = b as DbgDotNetValueImpl;
			if (ai == null || bi == null) {
				// If they're equal, they're both null
				return ai == bi ? (bool?)null : false;
			}
			if (Dispatcher.CheckAccess())
				return EqualsCore(ai, bi);
			return Equals2(ai, bi);

			bool? Equals2(DbgDotNetValueImpl a2, DbgDotNetValueImpl b2) =>
				Dispatcher.InvokeRethrow(() => EqualsCore(a2, b2));
		}

		bool? EqualsCore(DbgDotNetValueImpl a, DbgDotNetValueImpl b) {
			Dispatcher.VerifyAccess();
			return GetEquatableValue(a.Type, a.TryGetCorValue()).Equals3(GetEquatableValue(b.Type, b.TryGetCorValue()));
		}

		protected override void CloseCore(DbgDispatcher dispatcher) { }
	}
}
