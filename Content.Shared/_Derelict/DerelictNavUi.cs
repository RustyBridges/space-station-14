using Robust.Shared.Serialization;

namespace Content.Shared._Derelict;

[Serializable, NetSerializable]
public enum DerelictNavUiKey : byte
{
    Key,
}

/// <summary>
/// One button on the navigation uplink.
/// </summary>
[Serializable, NetSerializable]
public sealed class DerelictNavOption
{
    /// <summary>Sent back to the server when the button is confirmed.</summary>
    public string Id;

    public string Label;

    public DerelictNavOption(string id, string label)
    {
        Id = id;
        Label = label;
    }
}

[Serializable, NetSerializable]
public sealed class DerelictNavBuiState : BoundUserInterfaceState
{
    /// <summary>Markup describing quota, drops left and the site clock.</summary>
    public string Status;

    public List<DerelictNavOption> Options;

    public DerelictNavBuiState(string status, List<DerelictNavOption> options)
    {
        Status = status;
        Options = options;
    }
}

/// <summary>
/// Sent when a player confirms one of the uplink's buttons.
/// </summary>
[Serializable, NetSerializable]
public sealed class DerelictNavSelectMessage : BoundUserInterfaceMessage
{
    public string Id;

    public DerelictNavSelectMessage(string id)
    {
        Id = id;
    }
}
