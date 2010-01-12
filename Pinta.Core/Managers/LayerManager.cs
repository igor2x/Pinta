// 
// LayerManager.cs
//  
// Author:
//       Jonathan Pobst <monkey@jpobst.com>
// 
// Copyright (c) 2010 Jonathan Pobst
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;

namespace Pinta.Core
{
	public class LayerManager : IEnumerable<Layer>
	{
		private int layer_name_int = 2;
		private int current_layer = -1;

		private List<Layer> layers;

		// The checkerboard layer that represents transparent
		private Layer transparent_layer;
		// The layer for tools to use until their output is committed
		private Layer tool_layer;
		// The layer used for selections
		private Layer selection_layer;
		private int selection_layer_index;
		private Path selection_path;

		public LayerManager ()
		{
			layers = new List<Layer> ();
			
			tool_layer = CreateLayer ("Tool Layer");
			tool_layer.Hidden = true;
			
			selection_layer = CreateLayer ("Selection Layer");
			selection_layer.Hidden = true;
			
			ResetSelectionPath ();
			//selection_layer.Opacity = .5;
			
			transparent_layer = CreateLayer ("Transparent", 16, 16);
			transparent_layer.Tiled = true;
			
			// Create checkerboard background	
			using (Cairo.Context g = new Cairo.Context (transparent_layer.Surface)) {
				g.FillRectangle (new Rectangle (0, 0, 16, 16), new Color (1, 1, 1));
				g.FillRectangle (new Rectangle (8, 0, 8, 8), new Color (0.75, 0.75, 0.75));
				g.FillRectangle (new Rectangle (0, 8, 8, 8), new Color (0.75, 0.75, 0.75));
			}
		}

		#region Public Properties
		public Layer this[int index] {
			get { return layers[index]; }
		}

		public Layer CurrentLayer {
			get { return layers[current_layer]; }
		}

		public int Count {
			get { return layers.Count; }
		}

		public Layer ToolLayer {
			get { return tool_layer; }
		}

		public Layer TransparentLayer {
			get { return transparent_layer; }
		}

		public Layer SelectionLayer {
			get { return selection_layer; }
		}
		
		public int CurrentLayerIndex {
			get { return current_layer; }
		}
		
		public Path SelectionPath {
			get { return selection_path; }

			set {
				if (selection_path == value)
					return;
				
				if (selection_path != null)
					(selection_path as IDisposable).Dispose ();
				
				selection_path = value;
			}
		}

		public bool ShowSelection { get; set; }
		public bool ShowSelectionLayer { get; set; }
		#endregion

		#region Public Methods
		public void Clear ()
		{
			while (layers.Count > 0) {
				Layer l = layers[layers.Count - 1];
				layers.RemoveAt (layers.Count - 1);
				(l.Surface as IDisposable).Dispose ();
			}
			
			current_layer = -1;
			OnLayerRemoved ();
		}

		public List<Layer> GetLayersToPaint ()
		{
			List<Layer> paint = layers.Where (l => !l.Hidden).ToList ();
			
			if (!tool_layer.Hidden)
				paint.Add (tool_layer);
			if (ShowSelectionLayer)
				paint.Insert (selection_layer_index, selection_layer);
			
			return paint;
		}

		public void SetCurrentLayer (int i)
		{
			current_layer = i;
			
			OnSelectedLayerChanged ();
		}

		public void SetCurrentLayer (Layer layer)
		{
			current_layer = layers.IndexOf (layer);
			
			OnSelectedLayerChanged ();
		}

		// Adds a new layer above the current one
		public Layer AddNewLayer (string name)
		{
			Layer layer;
			
			if (string.IsNullOrEmpty (name))
				layer = CreateLayer ();
			else
				layer = CreateLayer (name);
			
			layers.Insert (current_layer + 1, layer);
			
			if (layers.Count == 1)
				current_layer = 0;
			
			OnLayerAdded ();
			return layer;
		}

		// Delete the current layer
		public void DeleteCurrentLayer ()
		{
			Layer layer = CurrentLayer;
			
			layers.RemoveAt (current_layer);
			(layer.Surface as IDisposable).Dispose ();
			
			// Only change this if this wasn't already the bottom layer
			if (current_layer > 0)
				current_layer--;
			
			OnLayerRemoved ();
		}

		// Duplicate current layer
		public Layer DuplicateCurrentLayer ()
		{
			Layer source = CurrentLayer;
			Layer layer = CreateLayer (source.Name + " copy");
			
			using (Cairo.Context g = new Cairo.Context (layer.Surface)) {
				g.SetSource (source.Surface);
				g.Paint ();
			}
			
			layer.Hidden = source.Hidden;
			layer.Opacity = source.Opacity;
			layer.Tiled = source.Tiled;
			
			layers.Insert (++current_layer, layer);
			OnLayerAdded ();
			
			return layer;
		}

		// Flatten current layer
		public void MergeCurrentLayerDown ()
		{
			if (current_layer == 0)
				throw new InvalidOperationException ("Cannot flatten layer because current layer is the bottom layer.");
			
			Layer source = CurrentLayer;
			Layer dest = layers[current_layer - 1];
			
			using (Cairo.Context g = new Cairo.Context (dest.Surface)) {
				g.SetSource (source.Surface);
				g.PaintWithAlpha (source.Opacity);
			}
			
			DeleteCurrentLayer ();
		}

		// Move current layer up
		public void MoveCurrentLayerUp ()
		{
			if (current_layer == layers.Count)
				throw new InvalidOperationException ("Cannot move layer up because current layer is the top layer.");
			
			Layer layer = CurrentLayer;
			layers.RemoveAt (current_layer);
			layers.Insert (++current_layer, layer);
			
			OnSelectedLayerChanged ();
		}

		// Move current layer down
		public void MoveCurrentLayerDown ()
		{
			if (current_layer == 0)
				throw new InvalidOperationException ("Cannot move layer down because current layer is the bottom layer.");
			Layer layer = CurrentLayer;
			layers.RemoveAt (current_layer);
			layers.Insert (--current_layer, layer);
			
			OnSelectedLayerChanged ();
		}

		// Flip current layer horizontally
		public void FlipCurrentLayerHorizontal ()
		{
			CurrentLayer.FlipHorizontal ();

			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Flip current layer vertically
		public void FlipCurrentLayerVertical ()
		{
			CurrentLayer.FlipVertical ();

			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Flip layer horizontally
		public void FlipLayerHorizontal (int layerIndex)
		{
			layers[layerIndex].FlipHorizontal ();

			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Flip layer vertically
		public void FlipLayerVertical (int layerIndex)
		{
			layers[layerIndex].FlipVertical ();

			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Flip image horizontally
		public void FlipImageHorizontal ()
		{
			foreach (var layer in layers)
				layer.FlipHorizontal ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Flip image vertically
		public void FlipImageVertical ()
		{
			foreach (var layer in layers)
				layer.FlipVertical ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		// Rotate image 180 degrees (flip H+V)
		public void RotateImage180 ()
		{
			foreach (var layer in layers)
				layer.Rotate180 ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		public void Invert ()
		{
			foreach (var layer in layers)
				layer.Invert ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}

		public void Sepia ()
		{
			foreach (var layer in layers)
				layer.Sepia ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}
	
		public void Desaturate ()
		{
			foreach (var layer in layers)
				layer.Desaturate ();
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}	
		
		public void RotateImageCW ()
		{
			foreach (var layer in layers)
				layer.Rotate90CW ();
			
			PintaCore.Workspace.ImageSize = new PointD (PintaCore.Workspace.ImageSize.Y, PintaCore.Workspace.ImageSize.X);
			PintaCore.Workspace.CanvasSize = new PointD (PintaCore.Workspace.CanvasSize.Y, PintaCore.Workspace.CanvasSize.X);

			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}
	
		public void RotateImageCCW ()
		{
			foreach (var layer in layers)
				layer.Rotate90CCW ();
			
			PintaCore.Workspace.ImageSize = new PointD (PintaCore.Workspace.ImageSize.Y, PintaCore.Workspace.ImageSize.X);
			PintaCore.Workspace.CanvasSize = new PointD (PintaCore.Workspace.CanvasSize.Y, PintaCore.Workspace.CanvasSize.X);
			
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();

		}
			
		// Flatten image
		public void FlattenImage ()
		{
			if (layers.Count < 2)
				throw new InvalidOperationException ("Cannot flatten image because there is only one layer.");
			
			Layer dest = layers[0];
			
			using (Cairo.Context g = new Cairo.Context (dest.Surface)) {
				for (int i = 1; i < layers.Count; i++) {
					Layer source = layers[i];
					g.SetSource (source.Surface);
					g.PaintWithAlpha (source.Opacity);
				}
			}
			
			current_layer = 0;
			
			while (layers.Count > 1) {
				Layer l = layers[1];
				
				layers.RemoveAt (1);
				(l.Surface as IDisposable).Dispose ();
			}
			
			OnLayerRemoved ();
			PintaCore.Chrome.DrawingArea.GdkWindow.Invalidate ();
		}
		
		public void CreateSelectionLayer ()
		{
			selection_layer = CreateLayer ();
			selection_layer_index = current_layer + 1;
		}
		
		public void DestroySelectionLayer ()
		{
			ShowSelectionLayer = false;
			SelectionLayer.Clear ();
		}

		public void ResetSelectionPath ()
		{
			// Create checkerboard background	
			using (Cairo.Context g = new Cairo.Context (selection_layer.Surface))
				SelectionPath = g.CreateRectanglePath (new Rectangle (0, 0, PintaCore.Workspace.ImageSize.X, PintaCore.Workspace.ImageSize.Y));
			
			ShowSelection = false;
		}

		public ImageSurface GetFlattenedImage ()
		{
			Cairo.ImageSurface surf = new Cairo.ImageSurface (Cairo.Format.Argb32, (int)PintaCore.Workspace.ImageSize.X, (int)PintaCore.Workspace.ImageSize.Y);

			using (Cairo.Context g = new Cairo.Context (surf)) {
				foreach (var layer in PintaCore.Layers.GetLayersToPaint ()) {
					g.SetSource (layer.Surface);
					g.Paint ();
				}
			}
			
			return surf;
		}
		#endregion

		#region Protected Methods
		protected void OnLayerAdded ()
		{
			if (LayerAdded != null)
				LayerAdded.Invoke (this, EventArgs.Empty);
		}

		protected void OnLayerRemoved ()
		{
			if (LayerRemoved != null)
				LayerRemoved.Invoke (this, EventArgs.Empty);
		}
		
		protected void OnSelectedLayerChanged ()
		{
			if (SelectedLayerChanged != null)
				SelectedLayerChanged.Invoke (this, EventArgs.Empty);
		}	
		#endregion

		#region Private Methods
		public Layer CreateLayer ()
		{
			return CreateLayer (string.Format ("Layer {0}", layer_name_int++));
		}

		private Layer CreateLayer (string name)
		{
			return CreateLayer (name, (int)PintaCore.Workspace.ImageSize.X, (int)PintaCore.Workspace.ImageSize.Y);
		}

		public Layer CreateLayer (string name, int width, int height)
		{
			Cairo.ImageSurface surface = new Cairo.ImageSurface (Cairo.Format.ARGB32, width, height);
			Layer layer = new Layer (surface) { Name = name };
			
			return layer;
		}
		#endregion

		#region Events
		public event EventHandler LayerAdded;
		public event EventHandler LayerRemoved;
		public event EventHandler SelectedLayerChanged;
		#endregion

		#region IEnumerable<Layer> implementation
		public IEnumerator<Layer> GetEnumerator ()
		{
			return layers.GetEnumerator ();
		}
		#endregion

		#region IEnumerable implementation
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator ()
		{
			return layers.GetEnumerator ();
		}
		#endregion
	}
}
