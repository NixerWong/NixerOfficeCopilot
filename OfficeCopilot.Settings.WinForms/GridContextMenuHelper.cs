using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using System;
using System.Windows.Forms;

namespace OfficeCopilot.Settings.WinForms
{
    internal static class GridContextMenuHelper
    {
        public static void AttachToDataRows(GridControl grid, GridView view, ContextMenuStrip contextMenu)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (contextMenu == null) throw new ArgumentNullException(nameof(contextMenu));

            grid.ContextMenuStrip = null;
            grid.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;

                var hitInfo = view.CalcHitInfo(e.Location);
                if (!hitInfo.InRowCell || !view.IsDataRow(hitInfo.RowHandle))
                {
                    grid.ContextMenuStrip = null;
                    return;
                }

                view.FocusedRowHandle = hitInfo.RowHandle;
                grid.ContextMenuStrip = contextMenu;
            };
        }
    }
}
