using System.Linq;
using System.Numerics;
using Content.Shared._Derelict;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Derelict;

/// <summary>
/// The navigation uplink: contract status on top, one button per available action below.
/// Every action needs a second click to confirm, since jumping and departing cannot be undone.
/// </summary>
public sealed class DerelictNavWindow : DefaultWindow
{
    public event Action<string>? OptionConfirmed;

    private readonly RichTextLabel _status;
    private readonly BoxContainer _options;
    private readonly Label _empty;

    private List<string> _shownIds = new();
    private string? _armed;

    public DerelictNavWindow()
    {
        Title = Loc.GetString("derelict-nav-window-title");
        MinSize = new Vector2(420, 260);

        _status = new RichTextLabel { Margin = new Thickness(4, 4, 4, 10) };
        _options = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        _empty = new Label
        {
            Text = Loc.GetString("derelict-nav-window-no-options"),
            Margin = new Thickness(4),
        };

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(6),
        };
        root.AddChild(_status);
        root.AddChild(_options);
        root.AddChild(_empty);
        Contents.AddChild(root);
    }

    public void Update(DerelictNavBuiState state)
    {
        if (FormattedMessage.TryFromMarkup(state.Status, out var message))
            _status.SetMessage(message);
        else
            _status.SetMessage(state.Status);

        // The state is refreshed every second for the clock; only rebuild the buttons when they change
        // so a half-confirmed click is not thrown away.
        var ids = state.Options.Select(o => o.Id).ToList();
        if (ids.SequenceEqual(_shownIds))
            return;

        _shownIds = ids;
        _armed = null;
        _options.RemoveAllChildren();
        _empty.Visible = state.Options.Count == 0;

        foreach (var option in state.Options)
        {
            var button = new Button { Text = option.Label, HorizontalExpand = true };
            var id = option.Id;
            var label = option.Label;
            button.OnPressed += _ =>
            {
                if (_armed == id)
                {
                    _armed = null;
                    button.Text = label;
                    OptionConfirmed?.Invoke(id);
                    return;
                }

                ResetLabels(state.Options);
                _armed = id;
                button.Text = Loc.GetString("derelict-nav-window-confirm", ("action", label));
            };
            _options.AddChild(button);
        }
    }

    private void ResetLabels(List<DerelictNavOption> options)
    {
        for (var i = 0; i < _options.ChildCount && i < options.Count; i++)
        {
            if (_options.GetChild(i) is Button button)
                button.Text = options[i].Label;
        }
    }
}
