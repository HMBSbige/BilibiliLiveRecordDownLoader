using QRCoder;
using System;
using System.Windows;
using System.Windows.Media;

namespace BilibiliLiveRecordDownLoader.Utils
{
	public class XamlQRCode : AbstractQRCode, IDisposable
	{
		public XamlQRCode(QRCodeData data) : base(data) { }

		public DrawingImage GetGraphic(int pixelsPerModule, bool drawQuietZones = true)
		{
			var drawableModulesCount = GetDrawableModulesCount(drawQuietZones);
			var viewBox = new Size(pixelsPerModule * drawableModulesCount, pixelsPerModule * drawableModulesCount);
			return GetGraphic(viewBox, new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.White), drawQuietZones);
		}

		public DrawingImage GetGraphic(Size viewBox, Brush darkBrush, Brush lightBrush, bool drawQuietZones = true)
		{
			var drawableModulesCount = GetDrawableModulesCount(drawQuietZones);
			var qrSize = Math.Min(viewBox.Width, viewBox.Height);
			var unitsPerModule = qrSize / drawableModulesCount;
			var offsetModules = drawQuietZones ? 0 : 4;

			var drawing = new DrawingGroup();
			drawing.Children.Add(new GeometryDrawing(lightBrush, null, new RectangleGeometry(new Rect(new Point(0, 0), new Size(qrSize, qrSize)))));

			var group = new GeometryGroup();
			var x = 0d;
			for (var xi = offsetModules; xi < drawableModulesCount + offsetModules; xi++)
			{
				var y = 0d;
				for (var yi = offsetModules; yi < drawableModulesCount + offsetModules; yi++)
				{
					if (QrCodeData.ModuleMatrix[yi][xi])
					{
						group.Children.Add(new RectangleGeometry(new Rect(x, y, unitsPerModule, unitsPerModule)));
					}
					y += unitsPerModule;
				}
				x += unitsPerModule;
			}
			drawing.Children.Add(new GeometryDrawing(darkBrush, null, group));

			return new DrawingImage(drawing);
		}

		private int GetDrawableModulesCount(bool drawQuietZones = true)
		{
			return QrCodeData.ModuleMatrix.Count - (drawQuietZones ? 0 : 8);
		}
	}
}
