using System.Data;

namespace CCZModStudio;

public sealed partial class MainForm
{
    private void ShowItemIconCatalogDialog()
    {
        if (_project == null)
        {
            MessageBox.Show(this, "\u8bf7\u5148\u6253\u5f00 MOD \u9879\u76ee\u76ee\u5f55\u3002", "\u63d0\u793a", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_currentItemEditorData is not DataTable itemData)
        {
            LoadItemEditor();
            if (_currentItemEditorData is not DataTable loadedItemData)
            {
                return;
            }

            itemData = loadedItemData;
        }

        if (!Validate() || (_itemEditorGrid.IsCurrentCellInEditMode && !_itemEditorGrid.EndEdit()))
        {
            return;
        }

        using var dialog = new ItemIconCatalogDialog(_project, itemData, _itemIconPreviewService);
        dialog.ShowDialog(this);
    }
}
