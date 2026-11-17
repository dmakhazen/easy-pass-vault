using System.Globalization;
using Android.Views;
using EasyPassVault.Core;
using static EasyPassVault.Droid.L;

namespace EasyPassVault.Droid;

public sealed partial class MainActivity
{
    // Kept while the vault is open, so returning from an item restores the query and the scroll position.
    private View? listScreen;
    private IReadOnlyList<VaultItem> allItems = [];
    private IReadOnlyList<FolderInfo> folders = [];
    private Dictionary<string, string> folderNames = [];
    private string query = "", folderId = "";
    private ItemKind kind = ItemKind.Login;
    private Button? entriesTab, filesTab;
    private Spinner? folderPicker;
    private View? filters;
    private TextView? emptyNote;
    private ItemAdapter? adapter;

    private void ShowList()
    {
        if (session is null) { Lock(null); return; }
        if (listScreen is null) BuildList(session);
        Show(listScreen!, null);
    }

    private void BuildList(VaultSession vault)
    {
        var culture = Culture;
        allItems = vault.Items;
        // The root comes first, the rest by full name: "Работа", "Работа / Почта".
        var names = VaultSorting.NameComparer(culture);
        folders = vault.Folders.OrderBy(folder => folder.ParentId is null ? 0 : 1).ThenBy(folder => folder.Name, names).ToList();
        folderNames = folders.ToDictionary(folder => folder.Id, folder => folder.ParentId is null ? T("Vault root") : folder.Name);
        query = ""; folderId = ""; kind = ItemKind.Login;

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        int side = this.Dp(16);
        column.SetPadding(side, this.Dp(12), side, 0);

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var name = Ui.Label(this, vaultName, 18, Ui.Ink, bold: true);
        name.SetSingleLine(true);
        name.Ellipsize = Android.Text.TextUtils.TruncateAt.Middle;
        header.AddView(name, Ui.Beside(this, 0, weight: 1));
        header.AddView(Ui.MakeButton(this, T("Settings"), primary: false, () => ShowSettings(ShowList), small: true), Ui.Beside(this, 12));
        header.AddView(Ui.MakeButton(this, T("Lock"), primary: false, () => Lock(null), small: true), Ui.Beside(this, 8));
        column.AddView(header);

        var search = Ui.Input(this, T("Search all folders"), password: false);
        search.TextChanged += (_, e) => { query = e.Text?.ToString() ?? ""; RefreshList(); };
        column.AddView(search, Ui.Below(this, 12));

        var filterColumn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        folderPicker = new Spinner(this, SpinnerMode.Dropdown) { Background = Ui.Box(this, Android.Graphics.Color.White, 12, Ui.Line) };
        var folderAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
            new[] { T("All folders") }.Concat(folders.Select(folder => folderNames[folder.Id])).ToList());
        folderAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        folderPicker.Adapter = folderAdapter;
        folderPicker.SetPadding(this.Dp(6), this.Dp(6), this.Dp(6), this.Dp(6));
        folderPicker.ItemSelected += (_, e) =>
        {
            string selected = e.Position == 0 ? "" : folders[e.Position - 1].Id;
            if (selected == folderId) return;
            folderId = selected;
            RefreshList();
        };
        filterColumn.AddView(folderPicker, Ui.Below(this, 10));

        var tabs = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        entriesTab = Ui.MakeButton(this, "", primary: true, () => SelectKind(ItemKind.Login), small: true);
        filesTab = Ui.MakeButton(this, "", primary: false, () => SelectKind(ItemKind.Text), small: true);
        tabs.AddView(entriesTab, Ui.Beside(this, 0, weight: 1));
        tabs.AddView(filesTab, Ui.Beside(this, 8, weight: 1));
        filterColumn.AddView(tabs, Ui.Below(this, 10));
        filters = filterColumn;
        column.AddView(filterColumn);

        emptyNote = Ui.Label(this, "", 15, Ui.Muted);
        emptyNote.Gravity = GravityFlags.Center;
        emptyNote.Visibility = ViewStates.Gone;
        column.AddView(emptyNote, Ui.Below(this, 32));

        adapter = new ItemAdapter(this);
        var list = new ListView(this) { Adapter = adapter, Divider = null, DividerHeight = 0 };
        list.SetClipToPadding(false);
        list.SetPadding(0, this.Dp(6), 0, this.Dp(16));
        list.ItemClick += (_, e) => ShowItem(adapter[e.Position].Id);
        column.AddView(list, Ui.Below(this, 6, height: 0, weight: 1));

        listScreen = column;
        RefreshList();
    }

    /// <summary>The title to show: an untitled item gets a placeholder in the chosen language.</summary>
    private static string TitleOf(VaultItem item) => string.IsNullOrWhiteSpace(item.Title) ? FromCore(item.DisplayTitle) : item.Title;

    private void SelectKind(ItemKind selected)
    {
        if (kind == selected) return;
        kind = selected;
        RefreshList();
    }

    private void RefreshList()
    {
        if (adapter is null || entriesTab is null || filesTab is null || filters is null || emptyNote is null) return;
        var culture = Culture;
        bool searching = query.Trim().Length > 0;
        // A query looks through every folder and both kinds; the folder and the tab apply only to the plain list.
        var inFolder = allItems.Where(item => folderId.Length == 0 || item.FolderId == folderId).ToList();
        IReadOnlyList<VaultItem> shown = searching
            ? VaultSearch.Find(allItems, query, culture, TitleOf)
            : VaultSorting.SortItems(inFolder.Where(item => item.Kind == kind), ItemSort.NameAscending, culture, TitleOf);

        filters.Visibility = searching ? ViewStates.Gone : ViewStates.Visible;
        StyleTab(entriesTab, $"{T("Entries")} · {inFolder.Count(item => item.Kind == ItemKind.Login)}", kind == ItemKind.Login);
        StyleTab(filesTab, $"{T("Files")} · {inFolder.Count(item => item.Kind == ItemKind.Text)}", kind == ItemKind.Text);

        adapter.Show(shown, folderNames, showFolder: searching || folderId.Length == 0, showKind: searching);
        emptyNote.Text = T(searching ? "Nothing found" : kind == ItemKind.Login ? "No entries here" : "No files here");
        emptyNote.Visibility = shown.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
    }

    private void StyleTab(Button tab, string text, bool selected)
    {
        tab.Text = text;
        tab.SetTextColor(selected ? Android.Graphics.Color.White : Ui.Green);
        var shape = Ui.Box(this, selected ? Ui.Green : Android.Graphics.Color.White, 10, selected ? null : Ui.Line);
        tab.Background = shape;
    }

    private sealed class ItemAdapter(MainActivity activity) : BaseAdapter<VaultItem>
    {
        private static readonly int TitleId = View.GenerateViewId(), DetailsId = View.GenerateViewId();
        private IReadOnlyList<VaultItem> items = [];
        private Dictionary<string, string> folderNames = [];
        private bool showFolder, showKind;

        public void Show(IReadOnlyList<VaultItem> shown, Dictionary<string, string> names, bool showFolder, bool showKind)
        {
            items = shown; folderNames = names; this.showFolder = showFolder; this.showKind = showKind;
            NotifyDataSetChanged();
        }

        public override VaultItem this[int position] => items[position];
        public override int Count => items.Count;
        public override long GetItemId(int position) => position;

        public override View GetView(int position, View? convertView, ViewGroup? parent)
        {
            var row = convertView as LinearLayout ?? NewRow();
            var item = items[position];
            row.FindViewById<TextView>(TitleId)!.Text = TitleOf(item);
            var parts = new List<string>();
            if (showKind) parts.Add(T(item.Kind == ItemKind.Text ? "File" : "Entry"));
            if (item.Kind == ItemKind.Login && item.Login.Length > 0) parts.Add(item.Login);
            if (showFolder && folderNames.TryGetValue(item.FolderId, out var folder)) parts.Add(folder);
            if (item.AttachmentCount > 0) parts.Add(T("attachments: {0}", item.AttachmentCount));
            var details = row.FindViewById<TextView>(DetailsId)!;
            details.Text = string.Join(" · ", parts);
            details.Visibility = parts.Count == 0 ? ViewStates.Gone : ViewStates.Visible;
            return row;
        }

        private LinearLayout NewRow()
        {
            // The outer layout only adds the gap between cards: list rows cannot have margins of their own.
            var row = new LinearLayout(activity) { Orientation = Orientation.Vertical };
            row.SetPadding(0, activity.Dp(4), 0, activity.Dp(4));
            var card = new LinearLayout(activity) { Orientation = Orientation.Vertical, Background = Ui.Box(activity, Android.Graphics.Color.White, 12, Ui.Line) };
            int horizontal = activity.Dp(14), vertical = activity.Dp(12);
            card.SetPadding(horizontal, vertical, horizontal, vertical);
            var title = Ui.Label(activity, "", 16, Ui.Ink, bold: true);
            title.Id = TitleId;
            title.SetSingleLine(true);
            title.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            card.AddView(title);
            var details = Ui.Label(activity, "", 13, Ui.Muted);
            details.Id = DetailsId;
            details.SetSingleLine(true);
            details.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            card.AddView(details, Ui.Below(activity, 2));
            row.AddView(card, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            return row;
        }
    }
}
