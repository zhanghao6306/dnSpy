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
using System.Diagnostics;
using dnSpy.Contracts.Debugger.DotNet.Text;
using dnSpy.Contracts.Text;
using dnSpy.Debugger.DotNet.Metadata;
using dnSpy.Roslyn.Properties;

namespace dnSpy.Roslyn.Debugger.ValueNodes.VisualBasic {
	sealed class VisualBasicValueNodeProviderFactory : DbgDotNetValueNodeProviderFactory {
		readonly DbgDotNetText instanceMembersName = new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Text, dnSpy_Roslyn_Resources.DebuggerVarsWindow_InstanceMembers));
		readonly DbgDotNetText staticMembersName = new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Text, dnSpy_Roslyn_Resources.DebuggerVarsWindow_VisualBasic_SharedMembers));
		protected override DbgDotNetText InstanceMembersName => instanceMembersName;
		protected override DbgDotNetText StaticMembersName => staticMembersName;

		public VisualBasicValueNodeProviderFactory(LanguageValueNodeFactory valueNodeFactory) : base(valueNodeFactory, isCaseSensitive: false) { }

		protected override bool HasNoChildren(DmdType type) {
			switch (DmdType.GetTypeCode(type)) {
			case TypeCode.Boolean:
			case TypeCode.Char:
			case TypeCode.SByte:
			case TypeCode.Byte:
			case TypeCode.Int16:
			case TypeCode.UInt16:
			case TypeCode.Int32:
			case TypeCode.UInt32:
			case TypeCode.Int64:
			case TypeCode.UInt64:
			case TypeCode.Single:
			case TypeCode.Double:
			case TypeCode.Decimal:
			case TypeCode.DateTime:
			case TypeCode.String:
				return true;

			case TypeCode.Empty:
			case TypeCode.Object:
			case TypeCode.DBNull:
			default:
				return false;
			}
		}

		protected override void FormatFieldName(ITextColorWriter output, DmdFieldInfo field) {
			var name = Formatters.VisualBasic.VisualBasicTypeFormatter.GetFormattedIdentifier(field.Name);
			var color = MemberUtils.GetColor(field);
			output.Write(color, name);
		}

		protected override void FormatPropertyName(ITextColorWriter output, DmdPropertyInfo property) {
			var name = Formatters.VisualBasic.VisualBasicTypeFormatter.GetFormattedIdentifier(property.Name);
			var color = MemberUtils.GetColor(property);
			output.Write(color, name);
		}

		protected override void FormatTypeName(ITextColorWriter output, DmdType type) {
			const Formatters.TypeFormatterOptions options = Formatters.TypeFormatterOptions.IntrinsicTypeKeywords | Formatters.TypeFormatterOptions.Namespaces;
			new Formatters.VisualBasic.VisualBasicTypeFormatter(output, options, null).Format(type, null);
		}

		const string ARRAY_PAREN_OPEN = "(";
		const string ARRAY_PAREN_CLOSE = ")";
		public override void FormatArrayName(ITextColorWriter output, int index) {
			output.Write(BoxedTextColor.Punctuation, ARRAY_PAREN_OPEN);
			output.Write(BoxedTextColor.Number, index.ToString());
			output.Write(BoxedTextColor.Punctuation, ARRAY_PAREN_CLOSE);
		}

		public override void FormatArrayName(ITextColorWriter output, int[] indexes) {
			Debug.Assert(indexes.Length > 0);
			output.Write(BoxedTextColor.Punctuation, ARRAY_PAREN_OPEN);
			for (int i = 0; i < indexes.Length; i++) {
				if (i > 0) {
					output.Write(BoxedTextColor.Punctuation, ",");
					output.Write(BoxedTextColor.Text, " ");
				}
				output.Write(BoxedTextColor.Number, indexes[i].ToString());
			}
			output.Write(BoxedTextColor.Punctuation, ARRAY_PAREN_CLOSE);
		}

		public override string GetNewObjectExpression(DmdConstructorInfo ctor, string argumentExpression, DmdType expectedType) {
			argumentExpression = LanguageValueNodeFactory.RemoveFormatSpecifiers(argumentExpression);
			var sb = ObjectCache.AllocStringBuilder();
			var output = new StringBuilderTextColorOutput(sb);
			output.Write(BoxedTextColor.Keyword, "New");
			output.Write(BoxedTextColor.Text, " ");
			FormatTypeName(output, ctor.DeclaringType);
			output.Write(BoxedTextColor.Punctuation, "(");
			var castType = ctor.GetMethodSignature().GetParameterTypes()[0];
			bool needCast = !expectedType.CanCastTo(castType);
			if (needCast) {
				output.Write(BoxedTextColor.Keyword, "CType");
				output.Write(BoxedTextColor.Punctuation, "(");
			}
			output.Write(BoxedTextColor.Text, argumentExpression);
			if (needCast) {
				output.Write(BoxedTextColor.Punctuation, ",");
				output.WriteSpace();
				new Formatters.VisualBasic.VisualBasicTypeFormatter(new StringBuilderTextColorOutput(sb), VisualBasicValueNodeFactory.TypeFormatterOptions, null).Format(castType, null);
				output.Write(BoxedTextColor.Punctuation, ")");
			}
			output.Write(BoxedTextColor.Punctuation, ")");
			return ObjectCache.FreeAndToString(ref sb);
		}

		public override string GetCallExpression(DmdMethodBase method, string instanceExpression) {
			instanceExpression = LanguageValueNodeFactory.RemoveFormatSpecifiers(instanceExpression);
			return instanceExpression + "." + method.Name + "()";
		}

		public override string GetDereferenceExpression(string instanceExpression) {
			instanceExpression = LanguageValueNodeFactory.RemoveFormatSpecifiers(instanceExpression);
			return "*" + instanceExpression;
		}

		public override ref readonly DbgDotNetText GetDereferencedName() => ref dereferencedName;
		static readonly DbgDotNetText dereferencedName = new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Punctuation, "*"));
	}
}
