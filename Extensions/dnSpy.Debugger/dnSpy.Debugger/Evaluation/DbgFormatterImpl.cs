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
using System.Globalization;
using dnSpy.Contracts.Debugger.Engine.Evaluation;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Contracts.Text;
using dnSpy.Debugger.CallStack;

namespace dnSpy.Debugger.Evaluation {
	sealed class DbgFormatterImpl : DbgFormatter {
		public override DbgLanguage Language { get; }

		readonly Guid runtimeKindGuid;
		readonly DbgEngineFormatter engineFormatter;

		public DbgFormatterImpl(DbgLanguage language, Guid runtimeKindGuid, DbgEngineFormatter engineFormatter) {
			Language = language ?? throw new ArgumentNullException(nameof(language));
			this.runtimeKindGuid = runtimeKindGuid;
			this.engineFormatter = engineFormatter ?? throw new ArgumentNullException(nameof(engineFormatter));
		}

		public override void FormatExceptionName(DbgEvaluationContext context, ITextColorWriter output, uint id) {
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (!(context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (context.Language != Language)
				throw new ArgumentException();
			if (context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			engineFormatter.FormatExceptionName(context, output, id);
		}

		public override void FormatStowedExceptionName(DbgEvaluationContext context, ITextColorWriter output, uint id) {
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (!(context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (context.Language != Language)
				throw new ArgumentException();
			if (context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			engineFormatter.FormatStowedExceptionName(context, output, id);
		}

		public override void FormatReturnValueName(DbgEvaluationContext context, ITextColorWriter output, uint id) {
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (!(context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (context.Language != Language)
				throw new ArgumentException();
			if (context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			engineFormatter.FormatReturnValueName(context, output, id);
		}

		public override void FormatObjectIdName(DbgEvaluationContext context, ITextColorWriter output, uint id) {
			if (context == null)
				throw new ArgumentNullException(nameof(context));
			if (!(context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (context.Language != Language)
				throw new ArgumentException();
			if (context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			engineFormatter.FormatObjectIdName(context, output, id);
		}

		public override void FormatFrame(DbgEvaluationInfo evalInfo, ITextColorWriter output, DbgStackFrameFormatterOptions options, DbgValueFormatterOptions valueOptions, CultureInfo cultureInfo) {
			if (evalInfo == null)
				throw new ArgumentNullException(nameof(evalInfo));
			if (!(evalInfo.Context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (evalInfo.Context.Language != Language)
				throw new ArgumentException();
			if (evalInfo.Context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			var frameImpl = evalInfo.Frame as DbgStackFrameImpl;
			if (frameImpl == null)
				throw new ArgumentException();
			if (!frameImpl.TryFormat(evalInfo.Context, output, options, valueOptions, cultureInfo, evalInfo.CancellationToken))
				engineFormatter.FormatFrame(evalInfo, output, options, valueOptions, cultureInfo);
		}

		public override void FormatValue(DbgEvaluationInfo evalInfo, ITextColorWriter output, DbgValue value, DbgValueFormatterOptions options, CultureInfo cultureInfo) {
			if (evalInfo == null)
				throw new ArgumentNullException(nameof(evalInfo));
			if (!(evalInfo.Context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (evalInfo.Context.Language != Language)
				throw new ArgumentException();
			if (evalInfo.Context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (!(value is DbgValueImpl valueImpl))
				throw new ArgumentException();
			if (value.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			engineFormatter.FormatValue(evalInfo, output, valueImpl.EngineValue, options, cultureInfo);
		}

		public override void FormatType(DbgEvaluationInfo evalInfo, ITextColorWriter output, DbgValue value, DbgValueFormatterTypeOptions options, CultureInfo cultureInfo) {
			if (evalInfo == null)
				throw new ArgumentNullException(nameof(evalInfo));
			if (!(evalInfo.Context is DbgEvaluationContextImpl))
				throw new ArgumentException();
			if (evalInfo.Context.Language != Language)
				throw new ArgumentException();
			if (evalInfo.Context.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			if (output == null)
				throw new ArgumentNullException(nameof(output));
			if (value == null)
				throw new ArgumentNullException(nameof(value));
			if (!(value is DbgValueImpl valueImpl))
				throw new ArgumentException();
			if (value.Runtime.RuntimeKindGuid != runtimeKindGuid)
				throw new ArgumentException();
			engineFormatter.FormatType(evalInfo, output, valueImpl.EngineValue, options, cultureInfo);
		}
	}
}
