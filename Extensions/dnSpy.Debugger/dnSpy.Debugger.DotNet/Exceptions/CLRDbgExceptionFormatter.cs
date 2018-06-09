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

using dnSpy.Contracts.Debugger.Exceptions;
using dnSpy.Contracts.Text;

namespace dnSpy.Debugger.DotNet.Exceptions {
	[ExportDbgExceptionFormatter(PredefinedExceptionCategories.DotNet)]
	sealed class CLRDbgExceptionFormatter : DbgExceptionFormatter {
		public override bool WriteName(ITextColorWriter writer, DbgExceptionDefinition definition) {
			var fullName = definition.Id.Name;
			if (!string.IsNullOrEmpty(fullName)) {
				var nsParts = fullName.Split(nsSeps);
				int pos = 0;
				var partColor = BoxedTextColor.Namespace;
				for (int i = 0; i < nsParts.Length - 1; i++) {
					var ns = nsParts[i];
					var sep = fullName[pos + ns.Length];
					if (sep == '+')
						partColor = BoxedTextColor.Type;
					writer.Write(partColor, ns);
					writer.Write(BoxedTextColor.Operator, sep.ToString());
					pos += ns.Length + 1;
				}
				writer.Write(BoxedTextColor.Type, nsParts[nsParts.Length - 1]);
			}
			return true;
		}
		static readonly char[] nsSeps = new char[] { '.', '+' };
	}
}
