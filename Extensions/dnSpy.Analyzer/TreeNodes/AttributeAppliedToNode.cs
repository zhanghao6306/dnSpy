// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
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
using dnSpy.Analyzer.Properties;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Text;

namespace dnSpy.Analyzer.TreeNodes {
	sealed class AttributeAppliedToNode : SearchNode {
		readonly TypeDef analyzedType;
		readonly string attributeName;

		AttributeTargets usage = AttributeTargets.All;
		bool allowMutiple;
		bool inherited = true;
		ConcurrentDictionary<MethodDef, int> foundMethods;

		public static bool CanShow(TypeDef type) => type.IsClass && IsCustomAttribute(type);

		static bool IsCustomAttribute(TypeDef type) {
			while (type != null) {
				var bt = type.BaseType.ResolveTypeDef();
				if (bt == null)
					return false;
				if (bt.FullName == "System.Attribute")
					return true;
				type = bt;
			}
			return false;
		}

		public AttributeAppliedToNode(TypeDef analyzedType) {
			this.analyzedType = analyzedType ?? throw new ArgumentNullException(nameof(analyzedType));
			attributeName = this.analyzedType.FullName;
			GetAttributeUsage();
		}

		void GetAttributeUsage() {
			if (analyzedType.HasCustomAttributes) {
				foreach (CustomAttribute ca in analyzedType.CustomAttributes) {
					ITypeDefOrRef t = ca.AttributeType;
					if (t != null && t.Name == "AttributeUsageAttribute" && t.Namespace == "System" &&
						ca.ConstructorArguments.Count > 0 &&
						ca.ConstructorArguments[0].Value is int) {
						usage = (AttributeTargets)ca.ConstructorArguments[0].Value;
						if (ca.ConstructorArguments.Count > 2) {
							if (ca.ConstructorArguments[1].Value is bool)
								allowMutiple = (bool)ca.ConstructorArguments[1].Value;
							if (ca.ConstructorArguments[2].Value is bool)
								inherited = (bool)ca.ConstructorArguments[2].Value;
						}
						foreach (var namedArgument in ca.Properties) {
							switch (namedArgument.Name) {
							case "AllowMultiple":
								if (namedArgument.Argument.Value is bool)
									allowMutiple = (bool)namedArgument.Argument.Value;
								break;
							case "Inherited":
								if (namedArgument.Argument.Value is bool)
									inherited = (bool)namedArgument.Argument.Value;
								break;
							}
						}
					}
				}
			}
		}

		protected override void Write(ITextColorWriter output, IDecompiler decompiler) =>
			output.Write(BoxedTextColor.Text, dnSpy_Analyzer_Resources.AppliedToTreeNode);

		protected override IEnumerable<AnalyzerTreeNodeData> FetchChildren(CancellationToken ct) {
			foundMethods = new ConcurrentDictionary<MethodDef, int>();

			//get the assemblies to search
			var currentAssembly = analyzedType.Module.Assembly;
			var modules = analyzedType.IsPublic ? GetReferencingModules(analyzedType.Module, ct) : GetModuleAndAnyFriends(analyzedType.Module, ct);

			var results = modules.AsParallel().WithCancellation(ct).SelectMany(a => FindReferencesInModule(new[] { a.Item1 }, a.Item2, ct));

			foreach (var result in results)
				yield return result;

			foundMethods = null;
		}

		IEnumerable<AnalyzerTreeNodeData> FindReferencesInModule(IEnumerable<ModuleDef> modules, ITypeDefOrRef tr, CancellationToken ct) {
			var checkedAsms = new HashSet<AssemblyDef>();
			foreach (var module in modules) {
				if ((usage & AttributeTargets.Assembly) != 0) {
					AssemblyDef asm = module.Assembly;
					if (asm != null && !checkedAsms.Contains(asm) && asm.HasCustomAttributes) {
						checkedAsms.Add(asm);
						foreach (var attribute in asm.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, tr)) {
								yield return new AssemblyNode(asm) { Context = Context };
								break;
							}
						}
					}
				}

				ct.ThrowIfCancellationRequested();

				if ((usage & AttributeTargets.Module) != 0) {
					if (module.HasCustomAttributes) {
						foreach (var attribute in module.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, tr)) {
								yield return new ModuleNode(module) { Context = Context };
								break;
							}
						}
					}
				}

				ct.ThrowIfCancellationRequested();

				foreach (TypeDef type in TreeTraversal.PreOrder(module.Types, t => t.NestedTypes)) {
					ct.ThrowIfCancellationRequested();
					foreach (var result in FindReferencesWithinInType(type, tr)) {
						ct.ThrowIfCancellationRequested();
						yield return result;
					}
				}
			}
		}

		IEnumerable<AnalyzerTreeNodeData> FindReferencesWithinInType(TypeDef type, ITypeDefOrRef attrTypeRef) {

			bool searchRequired = (type.IsClass && usage.HasFlag(AttributeTargets.Class))
				|| (type.IsEnum && usage.HasFlag(AttributeTargets.Enum))
				|| (type.IsInterface && usage.HasFlag(AttributeTargets.Interface))
				|| (type.IsValueType && usage.HasFlag(AttributeTargets.Struct));
			if (searchRequired) {
				if (type.HasCustomAttributes) {
					foreach (var attribute in type.CustomAttributes) {
						if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
							yield return new TypeNode(type) { Context = Context };
							break;
						}
					}
				}
			}

			if ((usage & AttributeTargets.GenericParameter) != 0 && type.HasGenericParameters) {
				foreach (var parameter in type.GenericParameters) {
					if (parameter.HasCustomAttributes) {
						foreach (var attribute in parameter.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
								yield return new TypeNode(type) { Context = Context };
								break;
							}
						}
					}
				}
			}

			if ((usage & AttributeTargets.Field) != 0 && type.HasFields) {
				foreach (var field in type.Fields) {
					if (field.HasCustomAttributes) {
						foreach (var attribute in field.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
								yield return new FieldNode(field) { Context = Context };
								break;
							}
						}
					}
				}
			}

			if (((usage & AttributeTargets.Property) != 0) && type.HasProperties) {
				foreach (var property in type.Properties) {
					if (property.HasCustomAttributes) {
						foreach (var attribute in property.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
								yield return new PropertyNode(property) { Context = Context };
								break;
							}
						}
					}
				}
			}
			if (((usage & AttributeTargets.Event) != 0) && type.HasEvents) {
				foreach (var _event in type.Events) {
					if (_event.HasCustomAttributes) {
						foreach (var attribute in _event.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
								yield return new EventNode(_event) { Context = Context };
								break;
							}
						}
					}
				}
			}

			if (type.HasMethods) {
				foreach (var method in type.Methods) {
					bool found = false;
					if ((usage & (AttributeTargets.Method | AttributeTargets.Constructor)) != 0) {
						if (method.HasCustomAttributes) {
							foreach (var attribute in method.CustomAttributes) {
								if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
									found = true;
									break;
								}
							}
						}
					}
					if (!found &&
						((usage & AttributeTargets.ReturnValue) != 0) &&
						method.Parameters.ReturnParameter.HasParamDef &&
						method.Parameters.ReturnParameter.ParamDef.HasCustomAttributes) {
						foreach (var attribute in method.Parameters.ReturnParameter.ParamDef.CustomAttributes) {
							if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
								found = true;
								break;
							}
						}
					}

					if (!found &&
						((usage & AttributeTargets.Parameter) != 0) &&
						method.Parameters.Count > 0) {
						foreach (var parameter in method.Parameters.Where(param => param.HasParamDef)) {
							if (parameter.IsHiddenThisParameter)
								continue;
							foreach (var attribute in parameter.ParamDef.CustomAttributes) {
								if (new SigComparer().Equals(attribute.AttributeType, attrTypeRef)) {
									found = true;
									break;
								}
							}
						}
					}

					if (found) {
						if (GetOriginalCodeLocation(method) is MethodDef codeLocation && !HasAlreadyBeenFound(codeLocation)) {
							yield return new MethodNode(codeLocation) { Context = Context };
						}
					}
				}
			}
		}

		bool HasAlreadyBeenFound(MethodDef method) => !foundMethods.TryAdd(method, 0);

		IEnumerable<Tuple<ModuleDef, ITypeDefOrRef>> GetReferencingModules(ModuleDef mod, CancellationToken ct) {
			var asm = mod.Assembly;
			if (asm == null) {
				yield return new Tuple<ModuleDef, ITypeDefOrRef>(mod, analyzedType);
				yield break;
			}

			foreach (var m in asm.Modules)
				yield return new Tuple<ModuleDef, ITypeDefOrRef>(m, analyzedType);

			var assemblies = Context.DocumentService.GetDocuments().Where(a => a.AssemblyDef != null);

			foreach (var assembly in assemblies) {
				ct.ThrowIfCancellationRequested();
				bool found = false;
				foreach (var reference in assembly.AssemblyDef.Modules.SelectMany(module => module.GetAssemblyRefs())) {
					if (AssemblyNameComparer.CompareAll.CompareTo(asm, reference) == 0) {
						found = true;
						break;
					}
				}
				if (found) {
					var typeref = GetScopeTypeRefInAssembly(assembly.AssemblyDef);
					if (typeref != null) {
						foreach (var m in assembly.AssemblyDef.Modules)
							yield return new Tuple<ModuleDef, ITypeDefOrRef>(m, typeref);
					}
				}
			}
		}

		IEnumerable<Tuple<ModuleDef, ITypeDefOrRef>> GetModuleAndAnyFriends(ModuleDef mod, CancellationToken ct) {
			var asm = mod.Assembly;
			if (asm == null) {
				yield return new Tuple<ModuleDef, ITypeDefOrRef>(mod, analyzedType);
				yield break;
			}

			foreach (var m in asm.Modules)
				yield return new Tuple<ModuleDef, ITypeDefOrRef>(m, analyzedType);

			if (asm.HasCustomAttributes) {
				var attributes = asm.CustomAttributes
					.Where(attr => attr.TypeFullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute");
				var friendAssemblies = new HashSet<string>();
				foreach (var attribute in attributes) {
					if (attribute.ConstructorArguments.Count == 0)
						continue;
					string assemblyName = attribute.ConstructorArguments[0].Value as UTF8String;
					if (assemblyName == null)
						continue;
					assemblyName = assemblyName.Split(',')[0]; // strip off any public key info
					friendAssemblies.Add(assemblyName);
				}

				if (friendAssemblies.Count > 0) {
					var assemblies = Context.DocumentService.GetDocuments().Where(a => a.AssemblyDef != null);

					foreach (var assembly in assemblies) {
						ct.ThrowIfCancellationRequested();
						if (friendAssemblies.Contains(assembly.AssemblyDef.Name)) {
							var typeref = GetScopeTypeRefInAssembly(assembly.AssemblyDef);
							if (typeref != null) {
								foreach (var m in assembly.AssemblyDef.Modules)
									yield return new Tuple<ModuleDef, ITypeDefOrRef>(m, typeref);
							}
						}
					}
				}
			}
		}

		ITypeDefOrRef GetScopeTypeRefInAssembly(AssemblyDef asm) {
			foreach (var mod in asm.Modules) {
				foreach (var typeref in mod.GetTypeRefs()) {
					if (new SigComparer().Equals(analyzedType, typeref))
						return typeref;
				}
			}
			return null;
		}
	}
	static class ExtensionMethods {
		public static bool HasCustomAttribute(this IMemberRef member, string attributeTypeName) => false;
	}
}
