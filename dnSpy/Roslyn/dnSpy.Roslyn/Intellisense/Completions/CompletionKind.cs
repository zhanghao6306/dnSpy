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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;

namespace dnSpy.Roslyn.Intellisense.Completions {
	enum CompletionKind {
		Unknown,
		ClassProtected,
		ClassInternal,
		ClassPrivate,
		Class,
		ConstantProtected,
		ConstantInternal,
		ConstantPrivate,
		Constant,
		DelegateProtected,
		DelegateInternal,
		DelegatePrivate,
		Delegate,
		EnumProtected,
		EnumInternal,
		EnumPrivate,
		Enum,
		EventProtected,
		EventInternal,
		EventPrivate,
		Event,
		ExtensionMethodProtected,
		ExtensionMethodInternal,
		ExtensionMethodPrivate,
		ExtensionMethod,
		FieldProtected,
		FieldInternal,
		FieldPrivate,
		Field,
		InterfaceProtected,
		InterfaceInternal,
		InterfacePrivate,
		Interface,
		MethodProtected,
		MethodInternal,
		MethodPrivate,
		Method,
		ModuleProtected,
		ModuleInternal,
		ModulePrivate,
		Module,
		OperatorProtected,
		OperatorInternal,
		OperatorPrivate,
		Operator,
		PropertyProtected,
		PropertyInternal,
		PropertyPrivate,
		Property,
		StructureProtected,
		StructureInternal,
		StructurePrivate,
		Structure,
		FileCSharp,
		FileVisualBasic,
		ProjectCSharp,
		ProjectVisualBasic,
		EnumMember,
		Assembly,
		RangeVariable,
		Local,
		Parameter,
		Intrinsic,
		Keyword,
		Label,
		Namespace,
		Folder,
		Reference,
		TypeParameter,
		Snippet,
		StatusError,
		StatusWarning,
		StatusInformation,
	}

	static class CompletionKindUtilities {
		public static CompletionKind ToCompletionKind(this ImmutableArray<string> tags) {
			foreach (var tag in tags) {
				switch (tag) {
				case CompletionTags.Class:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.ClassProtected;
					case Accessibility.Internal:	return CompletionKind.ClassInternal;
					case Accessibility.Private:		return CompletionKind.ClassPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Class;
					}

				case CompletionTags.Constant:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.ConstantProtected;
					case Accessibility.Internal:	return CompletionKind.ConstantInternal;
					case Accessibility.Private:		return CompletionKind.ConstantPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Constant;
					}

				case CompletionTags.Delegate:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.DelegateProtected;
					case Accessibility.Internal:	return CompletionKind.DelegateInternal;
					case Accessibility.Private:		return CompletionKind.DelegatePrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Delegate;
					}

				case CompletionTags.Enum:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.EnumProtected;
					case Accessibility.Internal:	return CompletionKind.EnumInternal;
					case Accessibility.Private:		return CompletionKind.EnumPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Enum;
					}

				case CompletionTags.Event:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.EventProtected;
					case Accessibility.Internal:	return CompletionKind.EventInternal;
					case Accessibility.Private:		return CompletionKind.EventPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Event;
					}

				case CompletionTags.ExtensionMethod:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.ExtensionMethodProtected;
					case Accessibility.Internal:	return CompletionKind.ExtensionMethodInternal;
					case Accessibility.Private:		return CompletionKind.ExtensionMethodPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.ExtensionMethod;
					}

				case CompletionTags.Field:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.FieldProtected;
					case Accessibility.Internal:	return CompletionKind.FieldInternal;
					case Accessibility.Private:		return CompletionKind.FieldPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Field;
					}

				case CompletionTags.Interface:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.InterfaceProtected;
					case Accessibility.Internal:	return CompletionKind.InterfaceInternal;
					case Accessibility.Private:		return CompletionKind.InterfacePrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Interface;
					}

				case CompletionTags.Method:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.MethodProtected;
					case Accessibility.Internal:	return CompletionKind.MethodInternal;
					case Accessibility.Private:		return CompletionKind.MethodPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Method;
					}

				case CompletionTags.Module:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.ModuleProtected;
					case Accessibility.Internal:	return CompletionKind.ModuleInternal;
					case Accessibility.Private:		return CompletionKind.ModulePrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Module;
					}

				case CompletionTags.Operator:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.OperatorProtected;
					case Accessibility.Internal:	return CompletionKind.OperatorInternal;
					case Accessibility.Private:		return CompletionKind.OperatorPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Operator;
					}

				case CompletionTags.Property:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.PropertyProtected;
					case Accessibility.Internal:	return CompletionKind.PropertyInternal;
					case Accessibility.Private:		return CompletionKind.PropertyPrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Property;
					}

				case CompletionTags.Structure:
					switch (GetAccessibility(tags)) {
					case Accessibility.Protected:	return CompletionKind.StructureProtected;
					case Accessibility.Internal:	return CompletionKind.StructureInternal;
					case Accessibility.Private:		return CompletionKind.StructurePrivate;
					case Accessibility.Public:
					case Accessibility.None:
					default:
						return CompletionKind.Structure;
					}

				case CompletionTags.File:
					switch (GetLanguage(tags)) {
					case Language.CSharp:			return CompletionKind.FileCSharp;
					case Language.VisualBasic:		return CompletionKind.FileVisualBasic;
					case Language.None:
					default:
						return CompletionKind.Unknown;
					}

				case CompletionTags.Project:
					switch (GetLanguage(tags)) {
					case Language.CSharp:			return CompletionKind.ProjectCSharp;
					case Language.VisualBasic:		return CompletionKind.ProjectVisualBasic;
					case Language.None:
					default:
						return CompletionKind.Unknown;
					}

				case CompletionTags.EnumMember:		return CompletionKind.EnumMember;
				case CompletionTags.Assembly:		return CompletionKind.Assembly;
				case CompletionTags.Parameter:		return CompletionKind.Parameter;
				case CompletionTags.RangeVariable:	return CompletionKind.RangeVariable;
				case CompletionTags.Intrinsic:		return CompletionKind.Intrinsic;
				case CompletionTags.Keyword:		return CompletionKind.Keyword;
				case CompletionTags.Label:			return CompletionKind.Label;
				case CompletionTags.Local:			return CompletionKind.Local;
				case CompletionTags.Namespace:		return CompletionKind.Namespace;
				case CompletionTags.Folder:			return CompletionKind.Folder;
				case CompletionTags.Reference:		return CompletionKind.Reference;
				case CompletionTags.TypeParameter:	return CompletionKind.TypeParameter;
				case CompletionTags.Snippet:		return CompletionKind.Snippet;
				case CompletionTags.Error:			return CompletionKind.StatusError;
				case CompletionTags.Warning:		return CompletionKind.StatusWarning;
				case "StatusInformation":			return CompletionKind.StatusInformation;
				}
			}
			return CompletionKind.Unknown;
		}

		static Accessibility GetAccessibility(ImmutableArray<string> tags) {
			if (tags.Contains(CompletionTags.Public))
				return Accessibility.Public;
			if (tags.Contains(CompletionTags.Protected))
				return Accessibility.Protected;
			if (tags.Contains(CompletionTags.Internal))
				return Accessibility.Internal;
			if (tags.Contains(CompletionTags.Private))
				return Accessibility.Private;
			return Accessibility.None;
		}

		static Language GetLanguage(ImmutableArray<string> tags) {
			if (tags.Contains(LanguageNames.CSharp))
				return Language.CSharp;
			if (tags.Contains(LanguageNames.VisualBasic))
				return Language.VisualBasic;
			return Language.None;
		}

		enum Accessibility {
			None,
			Public,
			Protected,
			Internal,
			Private,
		}

		enum Language {
			None,
			CSharp,
			VisualBasic,
		}
	}
}
