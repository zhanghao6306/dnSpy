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
using System.Linq;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.DotNet.Evaluation;
using dnSpy.Contracts.Debugger.DotNet.Evaluation.ExpressionCompiler;
using dnSpy.Contracts.Debugger.DotNet.Text;
using dnSpy.Contracts.Debugger.Engine.Evaluation;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;
using dnSpy.Debugger.DotNet.Evaluation.Engine.Interpreter;
using dnSpy.Debugger.DotNet.Properties;

namespace dnSpy.Debugger.DotNet.Evaluation.Engine {
	sealed class DbgEngineLocalsProviderImpl : DbgEngineLocalsValueNodeProvider {
		readonly DbgModuleReferenceProvider dbgModuleReferenceProvider;
		readonly DbgDotNetExpressionCompiler expressionCompiler;
		readonly DbgDotNetEngineValueNodeFactory valueNodeFactory;
		readonly DbgDotNetILInterpreter dnILInterpreter;
		readonly DbgAliasProvider dbgAliasProvider;

		public DbgEngineLocalsProviderImpl(DbgModuleReferenceProvider dbgModuleReferenceProvider, DbgDotNetExpressionCompiler expressionCompiler, DbgDotNetEngineValueNodeFactory valueNodeFactory, DbgDotNetILInterpreter dnILInterpreter, DbgAliasProvider dbgAliasProvider) {
			this.dbgModuleReferenceProvider = dbgModuleReferenceProvider ?? throw new ArgumentNullException(nameof(dbgModuleReferenceProvider));
			this.expressionCompiler = expressionCompiler ?? throw new ArgumentNullException(nameof(expressionCompiler));
			this.valueNodeFactory = valueNodeFactory ?? throw new ArgumentNullException(nameof(valueNodeFactory));
			this.dnILInterpreter = dnILInterpreter ?? throw new ArgumentNullException(nameof(dnILInterpreter));
			this.dbgAliasProvider = dbgAliasProvider ?? throw new ArgumentNullException(nameof(dbgAliasProvider));
		}

		public override DbgEngineLocalsValueNodeInfo[] GetNodes(DbgEvaluationInfo evalInfo, DbgValueNodeEvaluationOptions options, DbgLocalsValueNodeEvaluationOptions localsOptions) {
			var dispatcher = evalInfo.Runtime.GetDotNetRuntime().Dispatcher;
			if (dispatcher.CheckAccess())
				return GetNodesCore(evalInfo, options, localsOptions);
			return GetNodes(dispatcher, evalInfo, options, localsOptions);

			DbgEngineLocalsValueNodeInfo[] GetNodes(DbgDotNetDispatcher dispatcher2, DbgEvaluationInfo evalInfo2, DbgValueNodeEvaluationOptions options2, DbgLocalsValueNodeEvaluationOptions localsOptions2) =>
				dispatcher2.InvokeRethrow(() => GetNodesCore(evalInfo2, options2, localsOptions2));
		}

		enum ValueInfoKind {
			CompiledExpression,
			DecompilerGeneratedVariable,
		}

		abstract class ValueInfo {
			public abstract ValueInfoKind Kind { get; }
			public abstract bool IsParameter { get; }
		}

		sealed class CompiledExpressionValueInfo : ValueInfo {
			public override ValueInfoKind Kind => ValueInfoKind.CompiledExpression;
			public override bool IsParameter => compiledExpressions[index].ImageName == PredefinedDbgValueNodeImageNames.Parameter || compiledExpressions[index].ImageName == PredefinedDbgValueNodeImageNames.This;
			public bool IsCompilerGenerated => (compiledExpressions[index].ResultFlags & DbgDotNetCompiledExpressionResultFlags.CompilerGenerated) != 0;
			public ref DbgDotNetCompiledExpressionResult CompiledExpressionResult => ref compiledExpressions[index];

			readonly DbgDotNetCompiledExpressionResult[] compiledExpressions;
			readonly int index;

			public CompiledExpressionValueInfo(DbgDotNetCompiledExpressionResult[] compiledExpressions, int index) {
				this.compiledExpressions = compiledExpressions;
				this.index = index;
			}
		}

		sealed class DecompilerGeneratedVariableValueInfo : ValueInfo {
			public override ValueInfoKind Kind => ValueInfoKind.DecompilerGeneratedVariable;
			public override bool IsParameter => false;
			public string Name { get; }
			public DecompilerGeneratedVariableValueInfo(string name) =>
				Name = name ?? throw new ArgumentNullException(nameof(name));
		}

		sealed class GetNodesState {
			public readonly struct Key {
				readonly int decompilerSettingsVersion;
				// NOTE: DbgModule isn't part of this struct because the state is attached to the module.
				readonly int methodToken;
				readonly int methodVersion;
				readonly DbgModuleReference[] moduleReferences;
				readonly MethodDebugScope scope;
				readonly DbgValueNodeEvaluationOptions valueNodeEvaluationOptions;
				readonly DbgLocalsValueNodeEvaluationOptions localsValueNodeEvaluationOptions;
				public Key(int decompilerSettingsVersion, int methodToken, int methodVersion, DbgModuleReference[] moduleReferences, MethodDebugScope scope, DbgValueNodeEvaluationOptions valueNodeEvaluationOptions, DbgLocalsValueNodeEvaluationOptions localsValueNodeEvaluationOptions) {
					this.decompilerSettingsVersion = decompilerSettingsVersion;
					this.methodToken = methodToken;
					this.methodVersion = methodVersion;
					this.moduleReferences = moduleReferences;
					this.scope = scope;
					this.valueNodeEvaluationOptions = valueNodeEvaluationOptions;
					this.localsValueNodeEvaluationOptions = localsValueNodeEvaluationOptions;
				}
				public bool Equals(in Key other) =>
					scope == other.scope &&
					moduleReferences == other.moduleReferences &&
					methodToken == other.methodToken &&
					methodVersion == other.methodVersion &&
					decompilerSettingsVersion == other.decompilerSettingsVersion &&
					valueNodeEvaluationOptions == other.valueNodeEvaluationOptions &&
					localsValueNodeEvaluationOptions == other.localsValueNodeEvaluationOptions;
			}
			public Key CachedKey;
			public ValueInfo[] CachedValueInfos;
			public byte[] CachedAssemblyBytes;
			public DbgDotNetILInterpreterState CachedILInterpreterState;
			public int CachedDecompilerGeneratedCount;
			public int CachedCompilerGeneratedCount;
		}

		DbgEngineLocalsValueNodeInfo[] GetNodesCore(DbgEvaluationInfo evalInfo, DbgValueNodeEvaluationOptions options, DbgLocalsValueNodeEvaluationOptions localsOptions) {
			DbgEngineLocalsValueNodeInfo[] valueNodes = null;
			try {
				var module = evalInfo.Frame.Module;
				if (module == null)
					return Array.Empty<DbgEngineLocalsValueNodeInfo>();
				var languageDebugInfo = evalInfo.Context.TryGetLanguageDebugInfo();
				if (languageDebugInfo == null)
					return Array.Empty<DbgEngineLocalsValueNodeInfo>();
				var methodDebugInfo = languageDebugInfo.MethodDebugInfo;

				// All the variables windows use the same cached module references so make sure we pass
				// in the same arguments so it won't get recreated every time the method gets called.
				var info = dbgAliasProvider.GetAliases(evalInfo);
				var refsResult = dbgModuleReferenceProvider.GetModuleReferences(evalInfo.Runtime, evalInfo.Frame, info.typeReferences);
				if (refsResult.ErrorMessage != null)
					return new[] { CreateInternalErrorNode(evalInfo, refsResult.ErrorMessage) };

				// Since we attach this to the module, the module doesn't have to be part of Key
				var state = StateWithKey<GetNodesState>.GetOrCreate(module, this);
				var localsOptionsKey = localsOptions & ~(DbgLocalsValueNodeEvaluationOptions.ShowCompilerGeneratedVariables | DbgLocalsValueNodeEvaluationOptions.ShowDecompilerGeneratedVariables);
				var key = new GetNodesState.Key(methodDebugInfo.DecompilerSettingsVersion,
						methodDebugInfo.Method.MDToken.ToInt32(), languageDebugInfo.MethodVersion,
						refsResult.ModuleReferences, MethodDebugScopeUtils.GetScope(methodDebugInfo.Scope, languageDebugInfo.ILOffset),
						options, localsOptionsKey);

				var evalOptions = DbgEvaluationOptions.None;
				if ((options & DbgValueNodeEvaluationOptions.NoFuncEval) != 0)
					evalOptions |= DbgEvaluationOptions.NoFuncEval;
				if ((localsOptions & DbgLocalsValueNodeEvaluationOptions.ShowRawLocals) != 0)
					evalOptions |= DbgEvaluationOptions.RawLocals;

				ValueInfo[] valueInfos;
				byte[] assemblyBytes;
				int compilerGeneratedCount;
				int decompilerGeneratedCount;
				if (key.Equals(state.CachedKey)) {
					valueInfos = state.CachedValueInfos;
					assemblyBytes = state.CachedAssemblyBytes;
					decompilerGeneratedCount = state.CachedDecompilerGeneratedCount;
					compilerGeneratedCount = state.CachedCompilerGeneratedCount;
				}
				else {
					var compilationResult = expressionCompiler.CompileGetLocals(evalInfo, refsResult.ModuleReferences, evalOptions);
					evalInfo.CancellationToken.ThrowIfCancellationRequested();
					if (compilationResult.IsError)
						return new[] { CreateInternalErrorNode(evalInfo, compilationResult.ErrorMessage) };

					decompilerGeneratedCount = GetDecompilerGeneratedVariablesCount(methodDebugInfo.Scope, languageDebugInfo.ILOffset);

					valueInfos = new ValueInfo[compilationResult.CompiledExpressions.Length + decompilerGeneratedCount];
					int valueInfosIndex = 0;
					compilerGeneratedCount = 0;
					for (int i = 0; i < compilationResult.CompiledExpressions.Length; i++, valueInfosIndex++) {
						if ((compilationResult.CompiledExpressions[i].ResultFlags & DbgDotNetCompiledExpressionResultFlags.CompilerGenerated) != 0)
							compilerGeneratedCount++;
						valueInfos[valueInfosIndex] = new CompiledExpressionValueInfo(compilationResult.CompiledExpressions, i);
					}

					if (decompilerGeneratedCount > 0) {
						var scope = methodDebugInfo.Scope;
						for (;;) {
							foreach (var local in scope.Locals) {
								if (local.IsDecompilerGenerated) {
									valueInfos[valueInfosIndex] = new DecompilerGeneratedVariableValueInfo(local.Name);
									valueInfosIndex++;
								}
							}

							bool found = false;
							foreach (var childScope in scope.Scopes) {
								if (childScope.Span.Start <= languageDebugInfo.ILOffset && languageDebugInfo.ILOffset < childScope.Span.End) {
									found = true;
									scope = childScope;
									break;
								}
							}
							if (!found)
								break;
						}
					}

					if (valueInfos.Length != valueInfosIndex)
						throw new InvalidOperationException();

					assemblyBytes = compilationResult.Assembly;
					state.CachedKey = key;
					state.CachedValueInfos = valueInfos;
					state.CachedAssemblyBytes = assemblyBytes;
					state.CachedILInterpreterState = null;
					state.CachedDecompilerGeneratedCount = decompilerGeneratedCount;
					state.CachedCompilerGeneratedCount = compilerGeneratedCount;
				}

				int count = valueInfos.Length;
				if ((localsOptions & DbgLocalsValueNodeEvaluationOptions.ShowCompilerGeneratedVariables) == 0)
					count -= compilerGeneratedCount;
				if ((localsOptions & DbgLocalsValueNodeEvaluationOptions.ShowDecompilerGeneratedVariables) == 0)
					count -= decompilerGeneratedCount;
				valueNodes = count == 0 ? Array.Empty<DbgEngineLocalsValueNodeInfo>() : new DbgEngineLocalsValueNodeInfo[count];
				var valueCreator = new DbgDotNetValueCreator(valueNodeFactory, dnILInterpreter, evalInfo, options, evalOptions, assemblyBytes);
				int w = 0;
				for (int i = 0; i < valueInfos.Length; i++) {
					evalInfo.CancellationToken.ThrowIfCancellationRequested();
					var valueInfo = valueInfos[i];

					DbgEngineLocalsValueNodeInfo valueNodeInfo;
					switch (valueInfo.Kind) {
					case ValueInfoKind.CompiledExpression:
						var compExpr = (CompiledExpressionValueInfo)valueInfo;
						if ((localsOptions & DbgLocalsValueNodeEvaluationOptions.ShowCompilerGeneratedVariables) == 0 && compExpr.IsCompilerGenerated)
							continue;
						valueNodeInfo = new DbgEngineLocalsValueNodeInfo(
							compExpr.IsParameter ? DbgLocalsValueNodeKind.Parameter : DbgLocalsValueNodeKind.Local,
							valueCreator.CreateValueNode(ref state.CachedILInterpreterState, ref compExpr.CompiledExpressionResult));
						break;

					case ValueInfoKind.DecompilerGeneratedVariable:
						if ((localsOptions & DbgLocalsValueNodeEvaluationOptions.ShowDecompilerGeneratedVariables) == 0)
							continue;
						var decGen = (DecompilerGeneratedVariableValueInfo)valueInfo;
						valueNodeInfo = new DbgEngineLocalsValueNodeInfo(DbgLocalsValueNodeKind.Local,
							valueNodeFactory.CreateError(evalInfo,
							new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Local, decGen.Name)),
							dnSpy_Debugger_DotNet_Resources.DecompilerGeneratedVariablesCanNotBeEvaluated,
							decGen.Name, false));
						break;

					default:
						throw new InvalidOperationException();
					}

					valueNodes[w++] = valueNodeInfo;
				}
				if (w != valueNodes.Length)
					throw new InvalidOperationException();

				return valueNodes;
			}
			catch (Exception ex) {
				if (valueNodes != null)
					evalInfo.Runtime.Process.DbgManager.Close(valueNodes.Select(a => a.ValueNode).Where(a => a != null));
				if (!ExceptionUtils.IsInternalDebuggerError(ex))
					throw;
				return new[] { CreateInternalErrorNode(evalInfo, PredefinedEvaluationErrorMessages.InternalDebuggerError) };
			}
		}

		DbgEngineLocalsValueNodeInfo CreateInternalErrorNode(DbgEvaluationInfo evalInfo, string errorMessage) =>
			new DbgEngineLocalsValueNodeInfo(DbgLocalsValueNodeKind.Error, valueNodeFactory.CreateError(evalInfo, new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Text, "<error>")), errorMessage, "<internal.error>", false));

		static int GetDecompilerGeneratedVariablesCount(MethodDebugScope rootScope, uint offset) {
			var scope = rootScope;
			int count = 0;
			for (;;) {
				foreach (var local in scope.Locals) {
					if (local.IsDecompilerGenerated)
						count++;
				}

				bool found = false;
				foreach (var childScope in scope.Scopes) {
					if (childScope.Span.Start <= offset && offset < childScope.Span.End) {
						found = true;
						scope = childScope;
						break;
					}
				}
				if (!found)
					return count;
			}
		}
	}
}
