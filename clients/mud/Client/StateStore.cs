using System.Linq;

namespace WodMudClient;

public sealed class StateStore
{
    private readonly Dictionary<string, EntityInfo> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idByName = new(StringComparer.OrdinalIgnoreCase);

    public string? CurrentTargetId { get; private set; }
    public string? CurrentTargetName { get; private set; }
    public string? CurrentTargetToken => CurrentTargetId ?? CurrentTargetName;
    public double? CurrentHeading { get; private set; }
    public string? CurrentSpeed { get; private set; }
    public IReadOnlyList<string> AvailableDirections => _availableDirections;
    public Vector3? PlayerPosition { get; private set; }
    public ProximityRosterCache ProximityRoster { get; } = new();

    private readonly List<string> _availableDirections = new();

    public IReadOnlyCollection<EntityInfo> Entities => _entitiesById.Values;

    public void ResetEntities(IEnumerable<EntityInfo> entities)
    {
        _entitiesById.Clear();
        _idByName.Clear();
        foreach (var entity in entities)
        {
            AddOrUpdateEntity(entity);
        }
    }

    public void AddOrUpdateEntity(EntityInfo entity)
    {
        if (_entitiesById.TryGetValue(entity.Id, out var existing))
        {
            entity.Position ??= existing.Position;
            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                entity.Name = existing.Name;
            }
            if (string.IsNullOrWhiteSpace(entity.Type))
            {
                entity.Type = existing.Type;
            }
            if (string.IsNullOrWhiteSpace(entity.Description))
            {
                entity.Description = existing.Description;
            }
            entity.Bearing ??= existing.Bearing;
            entity.Elevation ??= existing.Elevation;
            entity.Range ??= existing.Range;
        }

        _entitiesById[entity.Id] = entity;
        if (!string.IsNullOrWhiteSpace(entity.Name))
        {
            _idByName[entity.Name] = entity.Id;
        }
    }

    public void RemoveEntity(string id)
    {
        if (_entitiesById.TryGetValue(id, out var entity))
        {
            _entitiesById.Remove(id);
            if (!string.IsNullOrWhiteSpace(entity.Name))
            {
                _idByName.Remove(entity.Name);
            }
        }

        if (string.Equals(CurrentTargetId, id, StringComparison.OrdinalIgnoreCase))
        {
            ClearTarget();
        }
    }

    public bool TryResolveTarget(string token, out string targetId, out string targetName)
    {
        targetId = string.Empty;
        targetName = string.Empty;

        if (_entitiesById.TryGetValue(token, out var entity))
        {
            targetId = entity.Id;
            targetName = entity.Name;
            return true;
        }

        if (_idByName.TryGetValue(token, out var id) && _entitiesById.TryGetValue(id, out entity))
        {
            targetId = entity.Id;
            targetName = entity.Name;
            return true;
        }

        return false;
    }

    public void SetTarget(string targetId, string targetName)
    {
        CurrentTargetId = targetId;
        CurrentTargetName = targetName;
    }

    public void SetTargetToken(string token)
    {
        CurrentTargetId = null;
        CurrentTargetName = token;
    }

    public void ClearTarget()
    {
        CurrentTargetId = null;
        CurrentTargetName = null;
    }

    public void UpdateMovementState(double? heading, string? speed, IEnumerable<string>? availableDirections)
    {
        if (heading.HasValue)
        {
            CurrentHeading = heading;
        }

        if (!string.IsNullOrWhiteSpace(speed))
        {
            CurrentSpeed = speed;
        }

        if (availableDirections != null)
        {
            _availableDirections.Clear();
            _availableDirections.AddRange(availableDirections);
        }
    }

    public void UpdatePlayerPosition(Vector3 position)
    {
        PlayerPosition = position;
    }
}

public sealed class EntityInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Vector3? Position { get; set; }
    public string? Description { get; set; }
    public double? Bearing { get; set; }
    public double? Elevation { get; set; }
    public double? Range { get; set; }
}

public sealed class ProximityRosterCache
{
    private readonly Dictionary<string, ProximityChannelCache> _channels = new(StringComparer.OrdinalIgnoreCase);

    public bool? DangerState { get; private set; }

    public void ApplyDelta(string channelName, ProximityChannelDelta delta)
    {
        var channel = GetOrCreateChannel(channelName);

        if (delta.Added.Count > 0)
        {
            foreach (var entity in delta.Added)
            {
                channel.Entities[entity.Id] = entity;
            }
        }

        if (delta.Removed.Count > 0)
        {
            foreach (var id in delta.Removed)
            {
                channel.Entities.Remove(id);
            }
        }

        if (delta.Updated.Count > 0)
        {
            foreach (var update in delta.Updated)
            {
                if (channel.Entities.TryGetValue(update.Id, out var existing))
                {
                    if (!string.IsNullOrWhiteSpace(update.Name))
                    {
                        existing.Name = update.Name;
                    }
                    if (!string.IsNullOrWhiteSpace(update.Type))
                    {
                        existing.Type = update.Type;
                    }
                    if (update.Bearing.HasValue)
                    {
                        existing.Bearing = update.Bearing.Value;
                    }
                    if (update.Elevation.HasValue)
                    {
                        existing.Elevation = update.Elevation.Value;
                    }
                    if (update.Range.HasValue)
                    {
                        existing.Range = update.Range.Value;
                    }
                }
                else
                {
                    channel.Entities[update.Id] = new ProximityEntity
                    {
                        Id = update.Id,
                        Name = update.Name ?? string.Empty,
                        Type = update.Type ?? "entity",
                        Bearing = update.Bearing ?? 0,
                        Elevation = update.Elevation ?? 0,
                        Range = update.Range ?? 0
                    };
                }
            }
        }

        if (delta.Count.HasValue)
        {
            channel.Count = delta.Count.Value;
        }

        if (delta.Sample != null)
        {
            channel.Sample = delta.Sample;
        }

        if (delta.LastSpeakerChanged)
        {
            channel.LastSpeaker = delta.LastSpeaker;
        }
    }

    public void UpdateDangerState(bool? dangerState)
    {
        if (dangerState.HasValue)
        {
            DangerState = dangerState.Value;
        }
    }

    public IReadOnlyList<ProximityEntity> GetEntitiesForNavigation()
    {
        var orderedChannels = new[] { "say", "shout", "see" };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ProximityEntity>();

        foreach (var channelName in orderedChannels)
        {
            if (!_channels.TryGetValue(channelName, out var channel))
            {
                continue;
            }

            foreach (var entity in channel.Entities.Values)
            {
                if (seen.Add(entity.Id))
                {
                    result.Add(entity);
                }
            }
        }

        return result;
    }

    public bool HasEntities()
    {
        return _channels.Values.Any(c => c.Entities.Count > 0);
    }

    private ProximityChannelCache GetOrCreateChannel(string name)
    {
        if (!_channels.TryGetValue(name, out var channel))
        {
            channel = new ProximityChannelCache(name);
            _channels[name] = channel;
        }

        return channel;
    }
}

public sealed class ProximityChannelCache
{
    public string Name { get; }
    public int Count { get; set; }
    public List<string>? Sample { get; set; }
    public string? LastSpeaker { get; set; }
    public Dictionary<string, ProximityEntity> Entities { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ProximityChannelCache(string name)
    {
        Name = name;
    }
}

public sealed class ProximityEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "entity";
    public double Bearing { get; set; }
    public double Elevation { get; set; }
    public double Range { get; set; }
}

public sealed class ProximityChannelDelta
{
    public List<ProximityEntity> Added { get; } = new();
    public List<string> Removed { get; } = new();
    public List<ProximityEntityDelta> Updated { get; } = new();
    public int? Count { get; set; }
    public List<string>? Sample { get; set; }
    public string? LastSpeaker { get; set; }
    public bool LastSpeakerChanged { get; set; }
}

public sealed class ProximityEntityDelta
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Type { get; set; }
    public double? Bearing { get; set; }
    public double? Elevation { get; set; }
    public double? Range { get; set; }
}
