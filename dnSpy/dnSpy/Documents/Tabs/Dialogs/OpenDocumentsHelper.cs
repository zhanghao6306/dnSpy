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
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using dnSpy.Contracts.Documents;
using dnSpy.Contracts.Documents.TreeView;

namespace dnSpy.Documents.Tabs.Dialogs {
	static class OpenDocumentsHelper {
		internal static IDsDocument[] OpenDocuments(IDocumentTreeView documentTreeView, Window ownerWindow, IEnumerable<string> filenames, bool selectDocument = true) {
			var documentLoader = new DsDocumentLoader(documentTreeView.DocumentService, ownerWindow);
			var loadedDocuments = documentLoader.Load(filenames.Select(a => new DocumentToLoad(DsDocumentInfo.CreateDocument(a))));
			var document = loadedDocuments.Length == 0 ? null : loadedDocuments[loadedDocuments.Length - 1];
			if (selectDocument && document != null) {
				Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
					var node = documentTreeView.FindNode(document);
					if (node != null)
						documentTreeView.TreeView.SelectItems(new[] { node });
				}));
			}
			return loadedDocuments;
		}
	}
}
