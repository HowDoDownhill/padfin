using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace Padfin;

/// <summary>Seerr requests and where each one stands, in a section per person: yours first.</summary>
public partial class RequestsScreen : Screen
{
    private RowPage _page;
    private Button _connect;
    private bool _loading;

    public override string Tab => "requests";
    public override string Title => "Requests";

    public override IEnumerable<(string, string)> Hints =>
        new[] { (Actions.Accept, "Select"), (Actions.X, "Refresh"), (Actions.Y, "Search") };

    public override void OnEnter()
    {
        _page = new RowPage();
        Ui.Fill(_page);
        AddChild(_page);
        _page.ShowMessage("Loading…");
        _ = LoadAsync();
    }

    public override void OnResume() => _ = LoadAsync();

    public override void FocusDefault()
    {
        if (IsInstanceValid(_connect) && _connect.IsInsideTree()) { _connect.GrabFocus(); }
        else { _page.Focus(); }
    }

    private async Task LoadAsync()
    {
        if (_loading) { return; }
        _loading = true;
        try
        {
            await Seerr.I.RestoreAsync();
            if (!IsInstanceValid(this)) { return; }
            if (!Seerr.I.Connected)
            {
                _page.Clear();
                var (panel, button) = SeerrSetup.Invitation(() => _ = LoadAsync());
                _page.AddBlock(panel);
                _connect = button;
                if (App.I.Current == this) { button.GrabFocus(); }
                return;
            }

            List<(SeerrRequest Request, SeerrItem Item)> requests;
            try { requests = await Seerr.I.RequestsAsync(); }
            catch (SeerrException e)
            {
                App.I.Toast(e.Message);
                return;
            }
            if (!IsInstanceValid(this)) { return; }

            var (focusRow, focusCard) = _page.FocusedPosition();
            _page.Clear();
            _connect = null;
            // One section per person: you first, then everyone else by name.
            var byUser = requests
                .GroupBy(r => r.Request.RequestedBy?.Id ?? -1)
                .Select(g => (Mine: g.Key == Seerr.I.User.Id, Name: g.First().Request.RequestedBy?.Name ?? "Unknown", Requests: g.ToList()))
                .OrderByDescending(g => g.Mine)
                .ThenBy(g => g.Name, System.StringComparer.OrdinalIgnoreCase);
            foreach (var group in byUser) { AddRow(group.Mine ? "Your Requests" : group.Name, group.Requests); }
            _page.Finish();

            SetInfo(requests.Count == 1 ? "1 request" : $"{requests.Count} requests");
            if (_page.Rows.Count == 0) { _page.ShowMessage("No requests yet. Search finds titles to request."); }
            else if (App.I.Current == this) { _page.Focus(focusRow, focusCard); }
        }
        finally { _loading = false; }
    }

    private void AddRow(string title, List<(SeerrRequest Request, SeerrItem Item)> requests)
    {
        // Requests waiting for approval lead each section, where a manager will look for them.
        var cards = requests.OrderBy(r => r.Request.Status == SeerrRequest.PendingApproval ? 0 : 1).Select(r =>
        {
            // The request carries the freshest status, so it replaces what the title details say.
            r.Item.MediaInfo ??= r.Request.Media;
            return new Card(r.Item, 220, r.Request.StatusText);
        });
        _page.Add(new CardRow(title, cards, card => App.I.Push(new SeerrDetailsScreen(card.Discovered))));
    }

    public override bool OnInput(InputEvent e)
    {
        if (e.IsActionPressed(Actions.X))
        {
            _ = LoadAsync();
            return true;
        }
        if (e.IsActionPressed(Actions.Y))
        {
            App.I.Push(new SearchScreen());
            return true;
        }
        return false;
    }

    public override MenuGroup PageMenu => new("Seerr", SeerrChoices());

    private List<(string, System.Action)> SeerrChoices()
    {
        var items = new List<(string, System.Action)>();
        if (Seerr.I.Connected)
        {
            items.Add(("Refresh", () => _ = LoadAsync()));
            items.Add(($"Disconnect from Seerr ({Seerr.I.User.Name})", () =>
            {
                Seerr.I.Disconnect();
                _ = LoadAsync();
            }));
        }
        else
        {
            items.Add(("Connect to Seerr", async () =>
            {
                if (await SeerrSetup.RunAsync()) { _ = LoadAsync(); }
            }));
        }
        return items;
    }
}
