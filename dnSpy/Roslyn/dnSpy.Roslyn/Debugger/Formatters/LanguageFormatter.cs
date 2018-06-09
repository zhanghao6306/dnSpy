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

using dnSpy.Contracts.Debugger.DotNet.Evaluation.Formatters;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Contracts.Text;

namespace dnSpy.Roslyn.Debugger.Formatters {
	abstract class LanguageFormatter : DbgDotNetFormatter {
		public override void FormatExceptionName(DbgEvaluationContext context, ITextColorWriter output, uint id) =>
			output.Write(BoxedTextColor.DebugExceptionName, AliasConstants.ExceptionName);

		public override void FormatStowedExceptionName(DbgEvaluationContext context, ITextColorWriter output, uint id) =>
			output.Write(BoxedTextColor.DebugStowedExceptionName, AliasConstants.StowedExceptionName);

		public override void FormatReturnValueName(DbgEvaluationContext context, ITextColorWriter output, uint id) {
			if (id == 0)
				output.Write(BoxedTextColor.DebugReturnValueName, AliasConstants.ReturnValueName);
			else
				output.Write(BoxedTextColor.DebugReturnValueName, AliasConstants.ReturnValueName + id.ToString());
		}

		public override void FormatObjectIdName(DbgEvaluationContext context, ITextColorWriter output, uint id) =>
			output.Write(BoxedTextColor.DebugObjectIdName, AliasConstants.ObjectIdName + id.ToString());
	}
}
