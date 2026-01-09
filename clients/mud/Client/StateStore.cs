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
}

public sealed class EntityInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
}
