namespace OpenHab.Core.Events;

public abstract record OpenHabEvent(string Topic, string Type);

public sealed record ItemStateChangedEvent(string ItemName, string State, string Topic, string Type)
    : OpenHabEvent(Topic, Type);

public sealed record ItemCommandEvent(string ItemName, string Command, string Topic, string Type)
    : OpenHabEvent(Topic, Type);
