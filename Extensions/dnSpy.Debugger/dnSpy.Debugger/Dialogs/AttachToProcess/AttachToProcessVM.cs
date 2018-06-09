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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.Attach.Dialogs;
using dnSpy.Contracts.MVVM;
using dnSpy.Contracts.Settings.AppearanceCategory;
using dnSpy.Contracts.Text;
using dnSpy.Contracts.Text.Classification;
using dnSpy.Contracts.ToolWindows.Search;
using dnSpy.Contracts.Utilities;
using dnSpy.Debugger.Properties;
using dnSpy.Debugger.UI;
using dnSpy.Debugger.Utilities;
using Microsoft.VisualStudio.Text.Classification;

namespace dnSpy.Debugger.Dialogs.AttachToProcess {
	sealed class AttachToProcessVM : ViewModelBase {
		readonly ObservableCollection<ProgramVM> realAllItems;
		public BulkObservableCollection<ProgramVM> AllItems { get; }
		public ObservableCollection<ProgramVM> SelectedItems { get; }

		public string SearchToolTip => ToolTipHelper.AddKeyboardShortcut(dnSpy_Debugger_Resources.AttachToProcess_Search_ToolTip, dnSpy_Debugger_Resources.ShortCutKeyCtrlF);
		public ICommand SearchHelpCommand => new RelayCommand(a => searchHelp());

		public ICommand RefreshCommand => new RelayCommand(a => Refresh(), a => CanRefresh);
		public string SearchHelpToolTip => ToolTipHelper.AddKeyboardShortcut(dnSpy_Debugger_Resources.SearchHelp_ToolTip, null);

		public ICommand InfoLinkCommand => new RelayCommand(a => ShowInfoLinkPage());
		public bool HasInfoLink => InfoLinkToolTip != null && infoLink != null;
		public string InfoLinkToolTip { get; }
		readonly string infoLink;

		public string Title { get; }

		public bool HasMessageText => !string.IsNullOrEmpty(MessageText);
		public string MessageText { get; }

		public string FilterText {
			get => filterText;
			set {
				if (filterText == value)
					return;
				filterText = value;
				OnPropertyChanged(nameof(FilterText));
				FilterList(filterText);
			}
		}
		string filterText = string.Empty;

		readonly UIDispatcher uiDispatcher;
		readonly DbgManager dbgManager;
		readonly AttachProgramOptionsAggregatorFactory attachProgramOptionsAggregatorFactory;
		readonly AttachToProcessContext attachToProcessContext;
		readonly Action searchHelp;
		readonly string[] providerNames;
		AttachProgramOptionsAggregator attachProgramOptionsAggregator;
		ProcessProvider processProvider;

		public AttachToProcessVM(ShowAttachToProcessDialogOptions options, UIDispatcher uiDispatcher, DbgManager dbgManager, DebuggerSettings debuggerSettings, ProgramFormatterProvider programFormatterProvider, IClassificationFormatMapService classificationFormatMapService, ITextElementProvider textElementProvider, AttachProgramOptionsAggregatorFactory attachProgramOptionsAggregatorFactory, Action searchHelp) {
			if (options == null) {
				options = new ShowAttachToProcessDialogOptions();
				options.InfoLink = new AttachToProcessLinkInfo {
					ToolTipMessage = dnSpy_Debugger_Resources.AttachToProcess_MakingAnImageEasierToDebug,
					Url = "https://github.com/0xd4d/dnSpy/wiki/Making-an-Image-Easier-to-Debug",
				};
			}
			Title = GetTitle(options);
			MessageText = GetMessage(options);
			if (options.InfoLink != null) {
				var l = options.InfoLink.Value;
				if (!string.IsNullOrEmpty(l.Url)) {
					InfoLinkToolTip = l.ToolTipMessage;
					infoLink = l.Url;
				}
			}

			providerNames = options.ProviderNames;
			realAllItems = new ObservableCollection<ProgramVM>();
			AllItems = new BulkObservableCollection<ProgramVM>();
			SelectedItems = new ObservableCollection<ProgramVM>();
			this.uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
			uiDispatcher.VerifyAccess();
			this.dbgManager = dbgManager ?? throw new ArgumentNullException(nameof(dbgManager));
			this.attachProgramOptionsAggregatorFactory = attachProgramOptionsAggregatorFactory ?? throw new ArgumentNullException(nameof(attachProgramOptionsAggregatorFactory));
			var classificationFormatMap = classificationFormatMapService.GetClassificationFormatMap(AppearanceCategoryConstants.UIMisc);
			attachToProcessContext = new AttachToProcessContext(classificationFormatMap, textElementProvider, new SearchMatcher(searchColumnDefinitions));
			this.searchHelp = searchHelp ?? throw new ArgumentNullException(nameof(searchHelp));

			attachToProcessContext.Formatter = programFormatterProvider.Create();
			attachToProcessContext.SyntaxHighlight = debuggerSettings.SyntaxHighlight;

			RefreshCore();
		}
		// Don't change the order of these instances without also updating input passed to SearchMatcher.IsMatchAll()
		static readonly SearchColumnDefinition[] searchColumnDefinitions = new SearchColumnDefinition[] {
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowProcess, "p", dnSpy_Debugger_Resources.Column_Process),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowPid, "i", dnSpy_Debugger_Resources.Column_ProcessID),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowTitle, "t", dnSpy_Debugger_Resources.Column_ProcessTitle),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowType, "T", dnSpy_Debugger_Resources.Column_ProcessType),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowMachine, "a", dnSpy_Debugger_Resources.Column_ProcessArchitecture),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowFullPath, "f", dnSpy_Debugger_Resources.Column_ProcessFilename),
			new SearchColumnDefinition(PredefinedTextClassifierTags.AttachToProcessWindowCommandLine, "c", dnSpy_Debugger_Resources.Column_ProcessCommandLine),
		};

		static string GetTitle(ShowAttachToProcessDialogOptions options) {
			var s = options.Title ?? dnSpy_Debugger_Resources.Attach_AttachToProcess;
			if (!string.IsNullOrEmpty(options.ProcessType))
				return s + " (" + options.ProcessType + ")";
			return s;
		}

		static string GetMessage(ShowAttachToProcessDialogOptions options) {
			if (options.Message != null)
				return options.Message;
			if (!Environment.Is64BitOperatingSystem)
				return null;
			return IntPtr.Size == 4 ? dnSpy_Debugger_Resources.Attach_UseDnSpy32 : dnSpy_Debugger_Resources.Attach_UseDnSpy64;
		}

		public string GetSearchHelpText() => attachToProcessContext.SearchMatcher.GetHelpText();

		public bool IsRefreshing => !CanRefresh;
		bool CanRefresh => attachProgramOptionsAggregator == null;
		void Refresh() {
			uiDispatcher.VerifyAccess();
			if (!CanRefresh)
				return;
			RefreshCore();
		}

		void RefreshCore() {
			uiDispatcher.VerifyAccess();
			RemoveAggregator();
			ClearAllItems();
			processProvider = new ProcessProvider();
			attachProgramOptionsAggregator = attachProgramOptionsAggregatorFactory.Create(providerNames);
			attachProgramOptionsAggregator.AttachProgramOptionsAdded += AttachProgramOptionsAggregator_AttachProgramOptionsAdded;
			attachProgramOptionsAggregator.Completed += AttachProgramOptionsAggregator_Completed;
			attachProgramOptionsAggregator.Start();
			OnPropertyChanged(nameof(IsRefreshing));
		}

		void RemoveAggregator() {
			uiDispatcher.VerifyAccess();
			processProvider?.Dispose();
			processProvider = null;
			if (attachProgramOptionsAggregator != null) {
				attachProgramOptionsAggregator.AttachProgramOptionsAdded -= AttachProgramOptionsAggregator_AttachProgramOptionsAdded;
				attachProgramOptionsAggregator.Completed -= AttachProgramOptionsAggregator_Completed;
				attachProgramOptionsAggregator.Dispose();
				attachProgramOptionsAggregator = null;
				OnPropertyChanged(nameof(IsRefreshing));
			}
		}

		void AttachProgramOptionsAggregator_AttachProgramOptionsAdded(object sender, AttachProgramOptionsAddedEventArgs e) {
			uiDispatcher.VerifyAccess();
			if (attachProgramOptionsAggregator != sender)
				return;
			foreach (var options in e.AttachProgramOptions) {
				if (!dbgManager.CanDebugRuntime(options.ProcessId, options.RuntimeId))
					continue;
				var vm = new ProgramVM(processProvider, options, attachToProcessContext);
				realAllItems.Add(vm);
				if (IsMatch(vm, filterText)) {
					int index = GetInsertionIndex(vm, AllItems);
					AllItems.Insert(index, vm);
				}
			}
		}

		int GetInsertionIndex(ProgramVM vm, IList<ProgramVM> list) {
			var comparer = ProgramVMComparer.Instance;
			int lo = 0, hi = list.Count - 1;
			while (lo <= hi) {
				int index = (lo + hi) / 2;

				int c = comparer.Compare(vm, list[index]);
				if (c < 0)
					hi = index - 1;
				else if (c > 0)
					lo = index + 1;
				else
					return index;
			}
			return hi + 1;
		}

		sealed class ProgramVMComparer : IComparer<ProgramVM> {
			public static readonly ProgramVMComparer Instance = new ProgramVMComparer();
			ProgramVMComparer() { }
			public int Compare(ProgramVM x, ProgramVM y) {
				var c = StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name);
				if (c != 0)
					return c;
				c = x.Id.CompareTo(y.Id);
				if (c != 0)
					return c;
				return StringComparer.CurrentCultureIgnoreCase.Compare(x.RuntimeName, y.RuntimeName);
			}
		}

		void AttachProgramOptionsAggregator_Completed(object sender, EventArgs e) {
			uiDispatcher.VerifyAccess();
			if (attachProgramOptionsAggregator != sender)
				return;
			RemoveAggregator();
		}

		void ClearAllItems() {
			uiDispatcher.VerifyAccess();
			realAllItems.Clear();
			AllItems.Reset(Array.Empty<ProgramVM>());
		}

		void FilterList(string filterText) {
			uiDispatcher.VerifyAccess();
			if (string.IsNullOrWhiteSpace(filterText))
				filterText = string.Empty;
			attachToProcessContext.SearchMatcher.SetSearchText(filterText);

			var newList = new List<ProgramVM>(GetFilteredItems(filterText));
			newList.Sort(ProgramVMComparer.Instance);
			AllItems.Reset(newList);
		}

		IEnumerable<ProgramVM> GetFilteredItems(string filterText) {
			uiDispatcher.VerifyAccess();
			foreach (var vm in realAllItems) {
				if (IsMatch(vm, filterText))
					yield return vm;
			}
		}

		bool IsMatch(ProgramVM vm, string filterText) {
			Debug.Assert(uiDispatcher.CheckAccess());
			// The order must match searchColumnDefinitions
			var allStrings = new string[] {
				GetProcess_UI(vm),
				GetPid_UI(vm),
				GetTitle_UI(vm),
				GetType_UI(vm),
				GetMachine_UI(vm),
				GetPath_UI(vm),
				GetCommandLine_UI(vm),
			};
			sbOutput.Reset();
			return attachToProcessContext.SearchMatcher.IsMatchAll(allStrings);
		}
		readonly StringBuilderTextColorOutput sbOutput = new StringBuilderTextColorOutput();

		string GetProcess_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WriteProcess(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetPid_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WritePid(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetTitle_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WriteTitle(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetType_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WriteType(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetMachine_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WriteMachine(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetPath_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WritePath(sbOutput, vm);
			return sbOutput.ToString();
		}

		string GetCommandLine_UI(ProgramVM vm) {
			Debug.Assert(uiDispatcher.CheckAccess());
			sbOutput.Reset();
			attachToProcessContext.Formatter.WriteCommandLine(sbOutput, vm);
			return sbOutput.ToString();
		}

		public void Copy(ProgramVM[] programs) {
			if (programs.Length == 0)
				return;

			var sb = new StringBuilderTextColorOutput();
			var formatter = attachToProcessContext.Formatter;
			foreach (var vm in programs) {
				formatter.WriteProcess(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WritePid(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WriteTitle(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WriteType(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WriteMachine(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WritePath(sb, vm);
				sb.Write(BoxedTextColor.Text, "\t");
				formatter.WriteCommandLine(sb, vm);
				sb.Write(BoxedTextColor.Text, Environment.NewLine);
			}

			var s = sb.ToString();
			if (s.Length > 0) {
				try {
					Clipboard.SetText(s);
				}
				catch (ExternalException) { }
			}
		}

		void ShowInfoLinkPage() {
			if (infoLink != null)
				OpenWebPage(infoLink);
		}

		static void OpenWebPage(string url) {
			try {
				Process.Start(url);
			}
			catch {
			}
		}

		internal void Dispose() {
			uiDispatcher.VerifyAccess();
			RemoveAggregator();
			ClearAllItems();
		}
	}
}
