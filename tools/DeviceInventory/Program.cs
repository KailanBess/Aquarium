using System.Text.Json;

namespace DeviceInventory;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            using var profile = InventoryWindow.OpenProfile();
            Application.Run(new InventoryWindow());
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Aquarium Device Inventory"); }
    }
}

internal sealed record Device(string Name, string Serial, string Receipt, DateOnly? Warranty, DateOnly? Maintenance);

internal sealed class InventoryWindow : Form
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AquariumDeviceInventory", "inventory.json");
    private readonly ListBox list = new() { Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 150 };
    private readonly TextBox serial = new() { Dock = DockStyle.Fill, MaxLength = 200 };
    private readonly TextBox receipt = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly DateTimePicker warranty = new() { ShowCheckBox = true, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker maintenance = new() { ShowCheckBox = true, Format = DateTimePickerFormat.Short };
    private readonly Label reminders = new() { AutoSize = true, Dock = DockStyle.Top };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 60_000 };
    private List<Device> devices;

    internal static FileStream OpenProfile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        try { return File.Open(StorePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("The inventory profile is in use or unavailable.", ex); }
    }

    internal InventoryWindow()
    {
        devices = File.Exists(StorePath)
            ? JsonSerializer.Deserialize<List<Device>>(File.ReadAllText(StorePath)) ?? throw new InvalidDataException("Inventory data is empty.")
            : [];
        Text = "Aquarium Device Inventory";
        Size = new Size(820, 460);
        MinimumSize = new Size(620, 420);
        list.AccessibleName = "Saved devices";
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        controls.Controls.Add(ActionButton("New", () => { list.ClearSelected(); ReadSelection(); }));
        controls.Controls.Add(ActionButton("Save", SaveDevice));
        controls.Controls.Add(ActionButton("Delete…", DeleteDevice));
        var receipts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        receipts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        receipts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        receipts.Controls.Add(receipt, 0, 0);
        var receiptActions = new FlowLayoutPanel { AutoSize = true };
        receiptActions.Controls.Add(ActionButton("Link…", PickReceipt));
        receiptActions.Controls.Add(ActionButton("Clear", receipt.Clear));
        receipts.Controls.Add(receiptActions, 1, 0);
        AddField(editor, "Device", name);
        AddField(editor, "Serial number", serial);
        AddField(editor, "Receipt file", receipts);
        AddField(editor, "Warranty ends", warranty);
        AddField(editor, "Maintenance due", maintenance);
        AddField(editor, "", controls);
        body.Controls.Add(list, 0, 0);
        body.Controls.Add(editor, 1, 0);
        Controls.Add(body);
        Controls.Add(reminders);
        list.SelectedIndexChanged += (_, _) => ReadSelection();
        timer.Tick += (_, _) => UpdateReminders();
        FormClosed += (_, _) => timer.Dispose();
        Reload(-1);
        timer.Start();
    }

    private static void AddField(TableLayoutPanel editor, string label, Control field)
    {
        int row = editor.RowCount++;
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row);
        editor.Controls.Add(field, 1, row);
    }

    private Button ActionButton(string caption, Action run)
    {
        var button = new Button { Text = caption, AutoSize = true };
        button.Click += (_, _) =>
        {
            try { run(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        return button;
    }

    private void ReadSelection()
    {
        var device = list.SelectedIndex >= 0 ? devices[list.SelectedIndex] : null;
        name.Text = device?.Name ?? "";
        serial.Text = device?.Serial ?? "";
        receipt.Text = device?.Receipt ?? "";
        SetDate(warranty, device?.Warranty);
        SetDate(maintenance, device?.Maintenance);
    }

    private static void SetDate(DateTimePicker picker, DateOnly? value)
    {
        picker.Value = value?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        picker.Checked = value.HasValue;
    }

    private void PickReceipt()
    {
        using var files = new OpenFileDialog { Title = "Link an existing receipt file", CheckFileExists = true };
        if (files.ShowDialog(this) == DialogResult.OK) receipt.Text = files.FileName;
    }

    private void SaveDevice()
    {
        if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidOperationException("Enter a device name.");
        DateOnly? DateOf(DateTimePicker picker) => picker.Checked ? DateOnly.FromDateTime(picker.Value) : null;
        var device = new Device(name.Text.Trim(), serial.Text.Trim(), receipt.Text, DateOf(warranty), DateOf(maintenance));
        int index = list.SelectedIndex;
        var next = devices.ToList();
        if (index < 0) { index = next.Count; next.Add(device); }
        else next[index] = device;
        Commit(next);
        Reload(index);
    }

    private void DeleteDevice()
    {
        int index = list.SelectedIndex;
        if (index < 0 || MessageBox.Show(this, "Remove this device record? The receipt file is kept.",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var next = devices.ToList();
        next.RemoveAt(index);
        Commit(next);
        Reload(Math.Min(index, next.Count - 1));
    }

    private void Commit(List<Device> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        var temporary = StorePath + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next));
            File.Move(temporary, StorePath, true);
            devices = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Reload(int index)
    {
        list.BeginUpdate();
        list.Items.Clear();
        list.Items.AddRange(devices.Select(device => device.Name).ToArray());
        list.SelectedIndex = index;
        list.EndUpdate();
        ReadSelection();
        UpdateReminders();
    }

    private void UpdateReminders()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        int due = devices.Count(device => device.Maintenance is { } date && date <= today);
        int warranties = devices.Count(device => device.Warranty is { } date && date >= today && date <= today.AddDays(30));
        reminders.Text = $"Maintenance due: {due}  •  Warranties ending in 30 days: {warranties}";
    }
}
