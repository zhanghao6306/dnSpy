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
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.DotNet.CorDebug;
using dnSpy.Contracts.Debugger.StartDebugging;
using dnSpy.Contracts.Debugger.StartDebugging.Dialog;
using dnSpy.Contracts.MVVM;
using dnSpy.Debugger.DotNet.CorDebug.Utilities;

namespace dnSpy.Debugger.DotNet.CorDebug.Dialogs.DebugProgram {
	sealed class DotNetFrameworkStartDebuggingOptionsPage : DotNetStartDebuggingOptionsPage {
		// This guid is also used by DebugProgramVM
		public override Guid Guid => new Guid("3FB8FCB5-AECE-443A-ABDE-601F2C23F1C1");
		public override double DisplayOrder => PredefinedStartDebuggingOptionsPageDisplayOrders.DotNetFramework;
		// Shouldn't be localized
		public override string DisplayName => ".NET Framework";

		public DotNetFrameworkStartDebuggingOptionsPage(IPickFilename pickFilename, IPickDirectory pickDirectory)
			: base(pickFilename, pickDirectory) {
		}

		protected override void PickNewFilename() {
			var newFilename = pickFilename.GetFilename(Filename, "exe", PickFilenameConstants.DotNetExecutableFilter);
			if (newFilename == null)
				return;

			Filename = newFilename;
		}

		public override void InitializePreviousOptions(StartDebuggingOptions options) {
			var dnfOptions = options as DotNetFrameworkStartDebuggingOptions;
			if (dnfOptions == null)
				return;
			Initialize(dnfOptions);
		}

		public override void InitializeDefaultOptions(string filename, string breakKind, StartDebuggingOptions options) =>
			Initialize(GetDefaultOptions(filename, breakKind, options));

		DotNetFrameworkStartDebuggingOptions GetDefaultOptions(string filename, string breakKind, StartDebuggingOptions options) {
			bool isExe = PortableExecutableFileHelpers.IsExecutable(filename);
			if (isExe) {
				var dnfOptions = CreateOptions(breakKind);
				Initialize(filename, dnfOptions);
				return dnfOptions;
			}
			else {
				// If it's a DLL, use the old EXE options if available
				if (options is DotNetFrameworkStartDebuggingOptions dnfOptions)
					return dnfOptions;
				return CreateOptions(breakKind);
			}
		}

		DotNetFrameworkStartDebuggingOptions CreateOptions(string breakKind) =>
			InitializeDefault(new DotNetFrameworkStartDebuggingOptions(), breakKind);

		void Initialize(DotNetFrameworkStartDebuggingOptions options) => base.Initialize(options);

		public override StartDebuggingOptionsInfo GetOptions() {
			var options = GetOptions(new DotNetFrameworkStartDebuggingOptions());
			return new StartDebuggingOptionsInfo(options, options.Filename);
		}

		public override bool SupportsDebugEngine(Guid engineGuid, out double order) {
			if (engineGuid == PredefinedGenericDebugEngineGuids.DotNetFramework) {
				order = PredefinedGenericDebugEngineOrders.DotNetFramework;
				return true;
			}

			order = 0;
			return false;
		}

		protected override bool CalculateIsValid() => string.IsNullOrEmpty(Verify(nameof(Filename)));

		protected override string Verify(string columnName) {
			if (columnName == nameof(Filename))
				return VerifyFilename(Filename);
			return string.Empty;
		}
	}
}
