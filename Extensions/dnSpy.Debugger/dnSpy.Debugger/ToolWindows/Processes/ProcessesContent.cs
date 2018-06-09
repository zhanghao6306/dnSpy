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
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using dnSpy.Contracts.Controls;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Utilities;
using dnSpy.Debugger.Properties;

namespace dnSpy.Debugger.ToolWindows.Processes {
	interface IProcessesContent : IUIObjectProvider {
		void OnShow();
		void OnClose();
		void OnVisible();
		void OnHidden();
		void Focus();
		ListView ListView { get; }
		ProcessesOperations Operations { get; }
	}

	[Export(typeof(IProcessesContent))]
	sealed class ProcessesContent : IProcessesContent {
		public object UIObject => processesControl;
		public IInputElement FocusedElement => processesControl.ListView;
		public FrameworkElement ZoomElement => processesControl;
		public ListView ListView => processesControl.ListView;
		public ProcessesOperations Operations { get; }

		readonly ProcessesControl processesControl;
		readonly IProcessesVM processesVM;

		sealed class ControlVM : ViewModelBase {
			public IProcessesVM VM { get; }
			ProcessesOperations Operations { get; }

			public string ContinueProcessToolTip => dnSpy_Debugger_Resources.Processes_ContinueProcessToolTip;
			public string BreakProcessToolTip => dnSpy_Debugger_Resources.Processes_BreakProcessToolTip;
			public string StepIntoProcessToolTip => $"{dnSpy_Debugger_Resources.Processes_StepIntoProcessToolTip} ({dnSpy_Debugger_Resources.StepsOnlyTheCurrentProcess})";
			public string StepOverProcessToolTip => $"{dnSpy_Debugger_Resources.Processes_StepOverProcessToolTip} ({dnSpy_Debugger_Resources.StepsOnlyTheCurrentProcess})";
			public string StepOutProcessToolTip => $"{dnSpy_Debugger_Resources.Processes_StepOutProcessToolTip} ({dnSpy_Debugger_Resources.StepsOnlyTheCurrentProcess})";
			public string DetachToolTip => ToolTipHelper.AddKeyboardShortcut(dnSpy_Debugger_Resources.Processes_DetachToolTip, null);
			public string TerminateToolTip => ToolTipHelper.AddKeyboardShortcut(dnSpy_Debugger_Resources.Processes_TerminateToolTip, null);
			public string AttachToProcessToolTip => ToolTipHelper.AddKeyboardShortcut(dnSpy_Debugger_Resources.Processes_AttachToProcessToolTip, dnSpy_Debugger_Resources.ShortCutKeyCtrlAltP);

			public ICommand ContinueProcessCommand => new RelayCommand(a => Operations.ContinueProcess(), a => Operations.CanContinueProcess);
			public ICommand BreakProcessCommand => new RelayCommand(a => Operations.BreakProcess(), a => Operations.CanBreakProcess);
			public ICommand StepIntoProcessCommand => new RelayCommand(a => Operations.StepIntoProcess(), a => Operations.CanStepIntoProcess);
			public ICommand StepOverProcessCommand => new RelayCommand(a => Operations.StepOverProcess(), a => Operations.CanStepOverProcess);
			public ICommand StepOutProcessCommand => new RelayCommand(a => Operations.StepOutProcess(), a => Operations.CanStepOutProcess);
			public ICommand DetachCommand => new RelayCommand(a => Operations.DetachProcess(), a => Operations.CanDetachProcess);
			public ICommand TerminateCommand => new RelayCommand(a => Operations.TerminateProcess(), a => Operations.CanTerminateProcess);
			public ICommand AttachToProcessCommand => new RelayCommand(a => Operations.AttachToProcess(), a => Operations.CanAttachToProcess);

			public ControlVM(IProcessesVM vm, ProcessesOperations operations) {
				VM = vm;
				Operations = operations;
			}
		}

		[ImportingConstructor]
		ProcessesContent(IWpfCommandService wpfCommandService, IProcessesVM processesVM, ProcessesOperations processesOperations) {
			Operations = processesOperations;
			processesControl = new ProcessesControl();
			this.processesVM = processesVM;
			processesControl.DataContext = new ControlVM(processesVM, processesOperations);
			processesControl.ProcessesListViewDoubleClick += ProcessesControl_ProcessesListViewDoubleClick;

			wpfCommandService.Add(ControlConstants.GUID_DEBUGGER_PROCESSES_CONTROL, processesControl);
			wpfCommandService.Add(ControlConstants.GUID_DEBUGGER_PROCESSES_LISTVIEW, processesControl.ListView);
		}

		void ProcessesControl_ProcessesListViewDoubleClick(object sender, EventArgs e) {
			bool newTab = Keyboard.Modifiers == ModifierKeys.Shift || Keyboard.Modifiers == ModifierKeys.Control;
			if (Operations.CanSetCurrentProcess)
				Operations.SetCurrentProcess(newTab);
		}

		public void Focus() => UIUtilities.FocusSelector(processesControl.ListView);
		public void OnClose() => processesVM.IsOpen = false;
		public void OnShow() => processesVM.IsOpen = true;
		public void OnHidden() => processesVM.IsVisible = false;
		public void OnVisible() => processesVM.IsVisible = true;
	}
}
