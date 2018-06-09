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
using System.IO;
using dnSpy.Contracts.Debugger.StartDebugging;
using dnSpy.Debugger.DotNet.CorDebug.Utilities;

namespace dnSpy.Debugger.DotNet.CorDebug.Impl {
	[ExportDbgProcessStarter(PredefinedDbgProcessStarterOrders.DotNetCore)]
	sealed class DotNetCoreDbgProcessStarter : DbgProcessStarter {
		string GetPathToDotNetExeHost() => DotNetCoreHelpers.GetPathToDotNetExeHost(IntPtr.Size * 8);

		public override bool IsSupported(string filename) =>
			DotNetCoreHelpers.IsDotNetCoreExecutable(filename) &&
			GetPathToDotNetExeHost() != null;

		public override bool TryStart(string filename, out string error) {
			var dotnetExeFilename = GetPathToDotNetExeHost();
			Debug.Assert(dotnetExeFilename != null);
			var startInfo = new ProcessStartInfo(dotnetExeFilename);
			startInfo.WorkingDirectory = Path.GetDirectoryName(filename);
			startInfo.Arguments = $"exec \"{filename}\"";
			startInfo.UseShellExecute = false;
			Process.Start(startInfo);
			error = null;
			return true;
		}
	}
}
