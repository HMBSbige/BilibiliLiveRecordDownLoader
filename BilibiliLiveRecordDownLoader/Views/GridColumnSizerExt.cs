using System.Linq;
using System.Windows;
using Syncfusion.UI.Xaml.Grid;

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
                length = DataGrid.View.Records.Where(recordEntry => recordEntry?.Data != null)
                        .Select(recordEntry => GetDisplayText(column, recordEntry.Data))
                        .Select(text => MeasureText(clientSize, text, column, null, GridQueryBounds.Width).Width).Prepend(length).Max();
            }

            return length;
        }

    }
}