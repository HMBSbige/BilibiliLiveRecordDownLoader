using Syncfusion.UI.Xaml.Grid;
using System.Linq;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Views
{
	public class GridColumnSizerExt : GridColumnSizer
	{
		public GridColumnSizerExt(SfDataGrid dataGrid) : base(dataGrid) { }

		protected override double CalculateCellWidth(GridColumn column, bool setWidth = true)
		{
			var length = base.CalculateCellWidth(column, setWidth);

			if (column is GridDateTimeColumn)
			{
				var clientSize = new Size(double.MaxValue, DataGrid.RowHeight);
				if (DataGrid.View != null)
				{
					length = DataGrid.View.Records.Where(recordEntry => recordEntry?.Data != null)
							.Select(recordEntry => GetDisplayText(column, recordEntry.Data))
							.Select(text => MeasureText(clientSize, text, column, null, GridQueryBounds.Width).Width)
							.Prepend(length).Max();
				}
			}

			return length;
		}

	}
}
