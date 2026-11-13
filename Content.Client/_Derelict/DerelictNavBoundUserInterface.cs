using Content.Shared._Derelict;
using Robust.Client.UserInterface;

namespace Content.Client._Derelict;

public sealed class DerelictNavBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private DerelictNavWindow? _window;

    public DerelictNavBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DerelictNavWindow>();
        _window.OptionConfirmed += id => SendMessage(new DerelictNavSelectMessage(id));

        if (State is DerelictNavBuiState state)
            _window.Update(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is DerelictNavBuiState navState)
            _window?.Update(navState);
    }
}
