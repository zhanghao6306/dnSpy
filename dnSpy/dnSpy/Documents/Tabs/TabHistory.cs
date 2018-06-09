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
using System.Diagnostics;
using dnSpy.Contracts.Documents.Tabs;

namespace dnSpy.Documents.Tabs {
	readonly struct TabContentState {
		public DocumentTabContent DocumentTabContent { get; }
		public object UIState { get; }

		public TabContentState(DocumentTabContent documentTabContent, object uiState) {
			DocumentTabContent = documentTabContent;
			UIState = uiState;
		}
	}

	sealed class TabHistory {
		readonly List<TabContentState> oldList;
		readonly List<TabContentState> newList;

		public DocumentTabContent Current => current;
		DocumentTabContent current;

		public TabHistory() {
			oldList = new List<TabContentState>();
			newList = new List<TabContentState>();
		}

		public void SetCurrent(DocumentTabContent content, bool saveCurrent) {
			if (saveCurrent && current != null)
				oldList.Add(new TabContentState(current, current.DocumentTab.UIContext.CreateUIState()));
			current = content ?? throw new ArgumentNullException(nameof(content));
			newList.Clear();
		}

		public void OverwriteCurrent(DocumentTabContent content) => current = content ?? throw new ArgumentNullException(nameof(content));

		public bool CanNavigateBackward => oldList.Count > 0;
		public bool CanNavigateForward => newList.Count > 0;

		public object NavigateBackward() {
			Debug.Assert(CanNavigateBackward);
			if (oldList.Count == 0)
				return null;
			var old = oldList[oldList.Count - 1];
			oldList.RemoveAt(oldList.Count - 1);
			newList.Add(new TabContentState(current, current.DocumentTab.UIContext.CreateUIState()));
			current = old.DocumentTabContent;
			return old.UIState;
		}

		public object NavigateForward() {
			Debug.Assert(CanNavigateForward);
			if (newList.Count == 0)
				return null;
			var old = newList[newList.Count - 1];
			newList.RemoveAt(newList.Count - 1);
			oldList.Add(new TabContentState(current, current.DocumentTab.UIContext.CreateUIState()));
			current = old.DocumentTabContent;
			return old.UIState;
		}

		public void RemoveFromBackwardList(Func<DocumentTabContent, bool> handler) => Remove(oldList, handler);
		public void RemoveFromForwardList(Func<DocumentTabContent, bool> handler) => Remove(newList, handler);

		void Remove(List<TabContentState> list, Func<DocumentTabContent, bool> handler) {
			for (int i = list.Count - 1; i >= 0; i--) {
				var c = list[i];
				if (handler(c.DocumentTabContent))
					list.RemoveAt(i);
			}
		}
	}
}
