using DevExpress.Blazor;
using Microsoft.JSInterop;
using System.Text.Json;

namespace ObracunDb.Services;

public class GridColumnWidthStorage(IJSRuntime jsRuntime)
{
    const string KeyPrefix = "ObracunDb.Grid.ColumnWidths.";

    public async Task LoadAsync(GridPersistentLayoutEventArgs e, string gridKey)
    {
        var json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", GetStorageKey(gridKey));
        if (string.IsNullOrWhiteSpace(json)) return;

        var widths = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (widths == null || widths.Count == 0) return;

        e.Layout = new GridPersistentLayout
        {
            Columns = new GridPersistentLayoutCollection<GridPersistentLayoutColumn>(
                widths.Select(w => new GridPersistentLayoutColumn
                {
                    ColumnType = GridPersistentLayoutColumnType.Data,
                    FieldName = w.Key,
                    Width = w.Value
                }))
        };
    }

    public async Task SaveAsync(GridPersistentLayoutEventArgs e, string gridKey)
    {
        var widths = e.Layout.Columns
            .Where(c => !string.IsNullOrWhiteSpace(c.FieldName) && !string.IsNullOrWhiteSpace(c.Width))
            .ToDictionary(c => c.FieldName, c => c.Width);

        await jsRuntime.InvokeVoidAsync(
            "localStorage.setItem",
            GetStorageKey(gridKey),
            JsonSerializer.Serialize(widths));
    }

    static string GetStorageKey(string gridKey) => KeyPrefix + gridKey;
}
