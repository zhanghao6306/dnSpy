﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnSpy.Analyzer.Properties;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace dnSpy.Analyzer.TreeNodes {
	sealed class VirtualMethodUsedByNode : SearchNode {
		readonly MethodDef analyzedMethod;
		ConcurrentDictionary<MethodDef, int> foundMethods;
		MethodDef baseMethod;
		List<ITypeDefOrRef> possibleTypes;

		public VirtualMethodUsedByNode(MethodDef analyzedMethod) => this.analyzedMethod = analyzedMethod ?? throw new ArgumentNullException(nameof(analyzedMethod));

		protected override void Write(ITextColorWriter output, IDecompiler decompiler) =>
			output.Write(BoxedTextColor.Text, dnSpy_Analyzer_Resources.UsedByTreeNode);

		protected override IEnumerable<AnalyzerTreeNodeData> FetchChildren(CancellationToken ct) {
			InitializeAnalyzer();

			var analyzer = new ScopedWhereUsedAnalyzer<AnalyzerTreeNodeData>(Context.DocumentService, analyzedMethod, FindReferencesInType);
			foreach (var child in analyzer.PerformAnalysis(ct)) {
				yield return child;
			}

			ReleaseAnalyzer();
		}

		void InitializeAnalyzer() {
			foundMethods = new ConcurrentDictionary<MethodDef, int>();

			var baseMethods = TypesHierarchyHelpers.FindBaseMethods(analyzedMethod).ToArray();
			if (baseMethods.Length > 0) {
				baseMethod = baseMethods[baseMethods.Length - 1];
			}
			else
				baseMethod = analyzedMethod;

			possibleTypes = new List<ITypeDefOrRef>();

			ITypeDefOrRef type = analyzedMethod.DeclaringType.BaseType;
			while (type != null) {
				possibleTypes.Add(type);
				type = type.ResolveTypeDef()?.BaseType;
			}
		}

		void ReleaseAnalyzer() {
			foundMethods = null;
			baseMethod = null;
		}

		IEnumerable<AnalyzerTreeNodeData> FindReferencesInType(TypeDef type) {
			string name = analyzedMethod.Name;
			foreach (MethodDef method in type.Methods) {
				if (!method.HasBody)
					continue;
				Instruction foundInstr = null;
				foreach (Instruction instr in method.Body.Instructions) {
					if (instr.Operand is IMethod mr && !mr.IsField && mr.Name == name) {
						// explicit call to the requested method 
						if ((instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt)
							&& Helpers.IsReferencedBy(analyzedMethod.DeclaringType, mr.DeclaringType)
							&& mr.ResolveMethodDef() == analyzedMethod) {
							foundInstr = instr;
							break;
						}
						// virtual call to base method
						if (instr.OpCode.Code == Code.Callvirt) {
							MethodDef md = mr.ResolveMethodDef();
							if (md == null) {
								// cannot resolve the operand, so ignore this method
								break;
							}
							if (md == baseMethod) {
								foundInstr = instr;
								break;
							}
						}
					}
				}

				if (foundInstr != null) {
					if (GetOriginalCodeLocation(method) is MethodDef codeLocation && !HasAlreadyBeenFound(codeLocation)) {
						var node = new MethodNode(codeLocation) { Context = Context };
						if (codeLocation == method)
							node.SourceRef = new SourceRef(method, foundInstr.Offset, foundInstr.Operand as IMDTokenProvider);
						yield return node;
					}
				}
			}
		}

		bool HasAlreadyBeenFound(MethodDef method) => !foundMethods.TryAdd(method, 0);
	}
}
