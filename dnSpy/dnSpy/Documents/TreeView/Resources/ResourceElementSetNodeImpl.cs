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
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Resources;
using dnSpy.Contracts.Decompiler;
using dnSpy.Contracts.Documents.Tabs.DocViewer;
using dnSpy.Contracts.Documents.TreeView;
using dnSpy.Contracts.Documents.TreeView.Resources;
using dnSpy.Contracts.Images;
using dnSpy.Contracts.TreeView;
using dnSpy.Properties;

namespace dnSpy.Documents.TreeView.Resources {
	[ExportResourceNodeProvider(Order = DocumentTreeViewConstants.ORDER_RSRCPROVIDER_RSRCELEMSET)]
	sealed class ResourceElementSetNodeProvider : IResourceNodeProvider {
		public ResourceNode Create(ModuleDef module, Resource resource, ITreeNodeGroup treeNodeGroup) {
			var er = resource as EmbeddedResource;
			if (er == null)
				return null;
			if (!ResourceReader.CouldBeResourcesFile(er.CreateReader()))
				return null;
			return new ResourceElementSetNodeImpl(treeNodeGroup, module, er);
		}

		public ResourceElementNode Create(ModuleDef module, ResourceElement resourceElement, ITreeNodeGroup treeNodeGroup) => null;
	}

	sealed class ResourceElementSetNodeImpl : ResourceElementSetNode {
		readonly ResourceElementSet resourceElementSet;
		readonly ModuleDef module;

		public override Guid Guid => new Guid(DocumentTreeViewConstants.RESOURCE_ELEMENT_SET_NODE_GUID);
		protected override ImageReference GetIcon() => DsImages.SourceFileGroup;

		public ResourceElementSetNodeImpl(ITreeNodeGroup treeNodeGroup, ModuleDef module, EmbeddedResource resource)
			: base(treeNodeGroup, resource) {
			this.module = module;
			resourceElementSet = ResourceReader.Read(module, resource.CreateReader());
		}

		public override void Initialize() => TreeNode.LazyLoading = true;

		public override IEnumerable<TreeNodeData> CreateChildren() {
			var treeNodeGroup = Context.DocumentTreeView.DocumentTreeNodeGroups.GetGroup(DocumentTreeNodeGroupType.ResourceElementTreeNodeGroup);
			foreach (var elem in resourceElementSet.ResourceElements)
				yield return Context.ResourceNodeFactory.Create(module, elem, treeNodeGroup);
		}

		protected override IEnumerable<ResourceData> GetDeserializedData() {
			TreeNode.EnsureChildrenLoaded();
			foreach (IResourceDataProvider node in TreeNode.DataChildren) {
				foreach (var data in node.GetResourceData(ResourceDataType.Deserialized))
					yield return data;
			}
		}

		public override void WriteShort(IDecompilerOutput output, IDecompiler decompiler, bool showOffset) {
			base.WriteShort(output, decompiler, showOffset);
			if (output is IDocumentViewerOutput documentViewerOutput) {
				documentViewerOutput.AddButton(dnSpy_Resources.SaveResourceButton, () => Save());
				documentViewerOutput.WriteLine();
				documentViewerOutput.WriteLine();
			}
		}

		public override void RegenerateEmbeddedResource() {
			var module = this.GetModule();
			Debug.Assert(module != null);
			if (module == null)
				throw new InvalidOperationException();
			RegenerateEmbeddedResource(module);
		}

		void RegenerateEmbeddedResource(ModuleDef module) {
			TreeNode.EnsureChildrenLoaded();
			var outStream = new MemoryStream();
			var resources = new ResourceElementSet();
			foreach (ResourceElementNode child in TreeNode.DataChildren)
				resources.Add(child.ResourceElement);
			ResourceWriter.Write(module, outStream, resources);
			Resource = new EmbeddedResource(Resource.Name, outStream.ToArray(), Resource.Attributes);
		}
	}
}
