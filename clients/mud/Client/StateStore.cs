using System;
using System.Linq;

namespace WodMudClient;

public sealed class StateStore
{
    private readonly Dictionary<string, EntityInfo> _entitiesById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _idByName = new(StringComparer.OrdinalIgnoreCase);

    public string? PlayerId { get; private set; }
    public string? CurrentTargetId { get; private set; }
    public string? CurrentTargetName { get; private set; }
    public string? CurrentTargetToken => CurrentTargetId ?? CurrentTargetName;
    public double? CurrentHeading { get; private set; }
    public string? CurrentSpeed { get; private set; }
    public IReadOnlyList<string> AvailableDirections => _availableDirections;
    public Vector3? PlayerPosition { get; private set; }
    public ProximityRosterCache ProximityRoster { get; } = new();
    public CombatState Combat { get; } = new();
    public VitalsState Vitals { get; } = new();
    public PartyState Party { get; } = new();

    private readonly List<string> _availableDirections = new();
    private DateTime? _lastAtbSampleAt;
    private int? _lastAtbSample;

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

    public void UpdatePlayerId(string? playerId)
    {
        if (!string.IsNullOrWhiteSpace(playerId))
        {
            PlayerId = playerId;
        }
    }

    public bool TryGetEntityName(string id, out string name)
    {
        name = string.Empty;
        if (_entitiesById.TryGetValue(id, out var entity))
        {
            name = string.IsNullOrWhiteSpace(entity.Name) ? entity.Id : entity.Name;
            return true;
        }

        return false;
    }

    public void UpdateCombatState(
        int? atbCurrent,
        int? atbMax,
        bool atbProvided,
        double? autoAttackCurrent,
        double? autoAttackMax,
        bool autoAttackProvided,
        bool? inCombat,
        bool inCombatProvided,
        string? autoAttackTarget,
        bool autoAttackTargetProvided)
    {
        if (atbProvided)
        {
            Combat.AtbCurrent = atbCurrent;
            Combat.AtbMax = atbMax;
            if (atbCurrent.HasValue)
            {
                var now = DateTime.UtcNow;
                if (_lastAtbSampleAt.HasValue && _lastAtbSample.HasValue)
                {
                    var seconds = (now - _lastAtbSampleAt.Value).TotalSeconds;
                    var delta = atbCurrent.Value - _lastAtbSample.Value;
                    if (seconds > 0 && delta >= 0)
                    {
                        Combat.AtbRatePerSecond = delta / seconds;
                    }
                    else if (delta < 0)
                    {
                        Combat.AtbRatePerSecond = null;
                    }
                }
                _lastAtbSampleAt = now;
                _lastAtbSample = atbCurrent.Value;
            }
        }

        if (autoAttackProvided)
        {
            Combat.AutoAttackCurrent = autoAttackCurrent;
            Combat.AutoAttackMax = autoAttackMax;
        }

        if (inCombatProvided)
        {
            Combat.InCombat = inCombat;
            if (inCombat == false)
            {
                Combat.AtbCurrent = null;
                Combat.AtbMax = null;
                Combat.AutoAttackCurrent = null;
                Combat.AutoAttackMax = null;
                Combat.AutoAttackTarget = null;
                Combat.AtbRatePerSecond = null;
                _lastAtbSampleAt = null;
                _lastAtbSample = null;
            }
        }

        if (autoAttackTargetProvided)
        {
            Combat.AutoAttackTarget = autoAttackTarget;
        }
    }

    public void ClearCombatState()
    {
        Combat.InCombat = false;
        Combat.AtbCurrent = null;
        Combat.AtbMax = null;
        Combat.AutoAttackCurrent = null;
        Combat.AutoAttackMax = null;
        Combat.AutoAttackTarget = null;
        Combat.AtbRatePerSecond = null;
        _lastAtbSampleAt = null;
        _lastAtbSample = null;
    }

    public void UpdateVitals(
        int? currentHp,
        int? maxHp,
        int? currentMana,
        int? maxMana,
        int? currentStamina,
        int? maxStamina)
    {
        if (currentHp.HasValue)
        {
            Vitals.CurrentHp = currentHp.Value;
        }
        if (maxHp.HasValue)
        {
            Vitals.MaxHp = maxHp.Value;
        }
        if (currentMana.HasValue)
        {
            Vitals.CurrentMana = currentMana.Value;
        }
        if (maxMana.HasValue)
        {
            Vitals.MaxMana = maxMana.Value;
        }
        if (currentStamina.HasValue)
        {
            Vitals.CurrentStamina = currentStamina.Value;
        }
        if (maxStamina.HasValue)
        {
            Vitals.MaxStamina = maxStamina.Value;
        }
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

public sealed class CombatState
{
    public int? AtbCurrent { get; set; }
    public int? AtbMax { get; set; }
    public double? AtbRatePerSecond { get; set; }
    public double? AutoAttackCurrent { get; set; }
    public double? AutoAttackMax { get; set; }
    public bool? InCombat { get; set; }
    public string? AutoAttackTarget { get; set; }
}

public sealed class VitalsState
{
    public int? CurrentHp { get; set; }
    public int? MaxHp { get; set; }
    public int? CurrentMana { get; set; }
    public int? MaxMana { get; set; }
    public int? CurrentStamina { get; set; }
    public int? MaxStamina { get; set; }
}

public sealed class PartyState
{
    private readonly Dictionary<string, PartyMember> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PartyAllyStatus> _allies = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PartyMember> Members => _members.Values
        .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string? LeaderId { get; private set; }

    public bool TryGetAlly(string id, out PartyAllyStatus status)
    {
        return _allies.TryGetValue(id, out status);
    }

    public void SetMembers(IEnumerable<string> names)
    {
        _members.Clear();
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _members[name] = new PartyMember { Name = name };
        }
    }

    public void SetLeader(string? leaderId)
    {
        LeaderId = string.IsNullOrWhiteSpace(leaderId) ? null : leaderId;
    }

    public void AddMember(string? id, string? name)
    {
        var key = !string.IsNullOrWhiteSpace(id) ? id! : name;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!_members.TryGetValue(key, out var member))
        {
            member = new PartyMember();
            _members[key] = member;
        }

        member.Id = id ?? member.Id;
        if (!string.IsNullOrWhiteSpace(name))
        {
            member.Name = name!;
        }
        else if (string.IsNullOrWhiteSpace(member.Name))
        {
            member.Name = key!;
        }
    }

    public void RemoveMember(string? id, string? name)
    {
        var key = !string.IsNullOrWhiteSpace(id) ? id! : name;
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _members.Remove(key!);
        if (!string.IsNullOrWhiteSpace(id))
        {
            _allies.Remove(id!);
        }
    }

    public void UpdateAllies(IEnumerable<PartyAllyStatus> allies)
    {
        _allies.Clear();
        foreach (var ally in allies)
        {
            if (string.IsNullOrWhiteSpace(ally.EntityId))
            {
                continue;
            }

            _allies[ally.EntityId] = ally;
        }
    }
}

public sealed class PartyMember
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PartyAllyStatus
{
    public string EntityId { get; set; } = string.Empty;
    public int? AtbCurrent { get; set; }
    public int? AtbMax { get; set; }
    public double? StaminaPct { get; set; }
    public double? ManaPct { get; set; }
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

        if (delta.SampleChanged)
        {
            channel.Sample = delta.Sample;
        }

        if (delta.LastSpeakerChanged)
        {
            channel.LastSpeaker = delta.LastSpeaker;
        }
    }

    public void ReplaceChannel(
        string channelName,
        IReadOnlyList<ProximityEntity> entities,
        int? count,
        List<string>? sample,
        bool sampleProvided,
        string? lastSpeaker,
        bool lastSpeakerProvided)
    {
        var channel = GetOrCreateChannel(channelName);
        channel.Entities.Clear();
        foreach (var entity in entities)
        {
            channel.Entities[entity.Id] = entity;
        }

        channel.Count = count ?? entities.Count;

        if (sampleProvided)
        {
            channel.Sample = sample;
        }
        else
        {
            channel.Sample = null;
        }

        if (lastSpeakerProvided)
        {
            channel.LastSpeaker = lastSpeaker;
        }
        else
        {
            channel.LastSpeaker = null;
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

    public IReadOnlyList<ProximityChannelSnapshot> GetChannelSnapshots()
    {
        return _channels.Values
            .OrderBy(channel => channel.Name, StringComparer.OrdinalIgnoreCase)
            .Select(channel => new ProximityChannelSnapshot(
                channel.Name,
                channel.Count,
                channel.Sample == null ? null : new List<string>(channel.Sample),
                channel.LastSpeaker,
                channel.Entities.Values
                    .OrderBy(entity => entity.Range)
                    .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(entity => new ProximityEntity
                    {
                        Id = entity.Id,
                        Name = entity.Name,
                        Type = entity.Type,
                        Bearing = entity.Bearing,
                        Elevation = entity.Elevation,
                        Range = entity.Range
                    })
                    .ToList()))
            .ToList();
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

public sealed record ProximityChannelSnapshot(
    string Name,
    int Count,
    List<string>? Sample,
    string? LastSpeaker,
    IReadOnlyList<ProximityEntity> Entities);

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
    public bool SampleChanged { get; set; }
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
