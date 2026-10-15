using System.Buffers;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Remote;

using static Ion.Extensions.Remote.RemoteSchema;

namespace Ion.Extensions.Ecs;

/// <summary>
/// The ECS module's remote methods (the Bevy Remote Protocol's <c>world.*</c> and <c>registry.schema</c>), registered by
/// <c>AddEcs</c> through <see cref="IRemoteMethodProvider"/> so the remote server needs no reference to the ECS.
/// </summary>
/// <remarks>
/// <para>
/// Remote-visible components are exactly the components of the <see cref="ComponentSerializerRegistry"/>: the built-in ones,
/// plus what the game registers with <c>AddEcsSerialization(r =&gt; r.Add(...))</c>. Their JSON is the world serializer's
/// (source-generated <c>JsonTypeInfo</c>, so NativeAOT-safe), with one difference: entity references (<c>Parent</c>,
/// <c>Children</c>, and fields using <see cref="EntityJsonConverter"/>) are entity ids rather than saved positions. Other
/// components are listed by type name (<c>world.list_components</c>) but cannot be read or written.
/// </para>
/// <para>
/// Entities are addressed by id (a number, as <c>world.query</c> returns) or by <see cref="EntityName"/> (a string).
/// Worlds: the root world and one per loaded scene (<see cref="EcsWorlds.Worlds"/>); methods take an optional <c>world</c>
/// index and default to the most recently created live world (the active scene's, or the root one).
/// </para>
/// </remarks>
internal sealed class EcsRemoteMethods(IServiceProvider services) : IRemoteMethodProvider
{
	private ComponentSerializerRegistry? _registry;

	private ComponentSerializerRegistry Registry => _registry ??= services.GetService<ComponentSerializerRegistry>() ?? ComponentSerializerRegistry.CreateDefault();

	private static readonly JsonObject WorldParam = Integer("The world index from world.list (default: the most recent world, the active scene's or the root one).");

	public void Register(RemoteMethodRegistry methods)
	{
		methods
			.Read("registry.schema", "The remote-visible ECS components (the world serializer's registry) with their JSON Schema.", Schema)
			.Read("world.list", "The live ECS worlds (the root world and one per loaded scene) with their entity counts.", ListWorlds)
			.Read("world.query", "Finds entities by components and name and returns their components as JSON.", Query,
				Object(
					("components?", Strings("Components to return for each entity (default: every remote-visible component it has).")),
					("with?", Strings("Only entities that have all of these components.")),
					("without?", Strings("Only entities that have none of these components.")),
					("name?", String("Only entities whose EntityName matches: exact, or a prefix ending in '*'.")),
					("limit?", Integer("At most this many entities (default 1000).")),
					("world?", WorldParam)), watchable: true)
			.Read("world.get_components", "Reads components of one entity.", GetComponents,
				Object(("entity", EntityRef), ("components?", Strings("Components to read (default: every remote-visible component).")), ("world?", WorldParam)), watchable: true)
			.Read("world.list_components", "Lists the components of one entity: remote-visible ones by name, others by type name.", ListComponents,
				Object(("entity", EntityRef), ("world?", WorldParam)))
			.Mutate("world.insert_components", "Adds components to an entity, or replaces them when present.", InsertComponents,
				Object(("entity", EntityRef), ("components", new JsonObject { ["type"] = "object", ["description"] = "Component name to JSON value, for example {\"Transform2D\": {\"Position\": [10, 20]}}." }), ("world?", WorldParam)))
			.Mutate("world.mutate_components", "Sets one field of a component, by path (dot separated, array indexes as numbers: 'Position.0').", MutateComponent,
				Object(("entity", EntityRef), ("component", String("The component name.")), ("path", String("The field path inside the component's JSON; empty replaces the whole component.")), ("value", Any("The new value.")), ("world?", WorldParam)))
			.Mutate("world.remove_components", "Removes components from an entity.", RemoveComponents,
				Object(("entity", EntityRef), ("components", Strings("The component names.")), ("world?", WorldParam)))
			.Mutate("world.spawn", "Creates an entity with components (and an optional EntityName); returns its id.", Spawn,
				Object(("components?", new JsonObject { ["type"] = "object", ["description"] = "Component name to JSON value." }), ("name?", String("An EntityName for the new entity.")), ("world?", WorldParam)))
			.Mutate("world.despawn", "Destroys an entity.", Despawn,
				Object(("entity", EntityRef), ("world?", WorldParam)));
	}

	private JsonNode Schema(RemoteRequest request)
	{
		var list = new JsonArray();
		foreach (var serializer in Registry.Items)
		{
			list.Add((JsonNode)new JsonObject
			{
				["name"] = serializer.Name,
				["type"] = serializer.ComponentType.Type.FullName,
				["tag"] = serializer.IsTag,
				["schema"] = serializer.Schema(),
			});
		}

		return new JsonObject { ["components"] = list, ["entityReferences"] = "Entity fields are entity ids (-1 for none)." };
	}

	private JsonNode ListWorlds(RemoteRequest request)
	{
		var worlds = Worlds(request);
		var list = new JsonArray();
		for (var i = 0; i < worlds.Count; i++)
		{
			list.Add((JsonNode)new JsonObject { ["index"] = i, ["entities"] = worlds[i].Size, ["default"] = i == worlds.Count - 1 });
		}

		return list;
	}

	private JsonNode Query(RemoteRequest request)
	{
		var world = WorldOf(request);
		var wanted = Resolve(request.GetStrings("components"));
		var with = Resolve(request.GetStrings("with"));
		var without = Resolve(request.GetStrings("without"));
		var name = request.GetOptionalString("name");
		var limit = request.GetInt32("limit", 1000, 1, 100_000);

		var description = new QueryDescription();
		var entities = new JsonArray();
		var total = 0;
		foreach (ref var chunk in world.Query(in description))
		{
			var archetypeOk = true;
			foreach (var s in with) archetypeOk &= chunk.Has(s.ComponentType);
			foreach (var s in without) archetypeOk &= !chunk.Has(s.ComponentType);
			if (!archetypeOk) continue;

			for (var i = 0; i < chunk.Count; i++)
			{
				var entity = chunk.Entity(i);
				var entityName = world.TryGet<EntityName>(entity, out var n) ? n.Value : null;
				if (name is not null && !NameMatches(entityName, name)) continue;
				total++;
				if (entities.Count >= limit) continue;
				entities.Add((JsonNode)Describe(world, entity, entityName, wanted.Count > 0 ? wanted : null));
			}
		}

		return new JsonObject { ["entities"] = entities, ["total"] = total, ["truncated"] = total > entities.Count };
	}

	private JsonNode GetComponents(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		var wanted = Resolve(request.GetStrings("components"));
		foreach (var s in wanted)
		{
			if (!s.Has(world, entity)) throw RemoteException.NotFound($"Entity {entity.Id} has no {s.Name} component.");
		}

		return Describe(world, entity, world.TryGet<EntityName>(entity, out var n) ? n.Value : null, wanted.Count > 0 ? wanted : null);
	}

	private JsonNode ListComponents(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		var visible = new JsonArray();
		var other = new JsonArray();
		foreach (var type in world.GetSignature(entity).Components)
		{
			if (Registry.Find(type) is { } serializer) visible.Add((JsonNode)serializer.Name);
			else other.Add((JsonNode)type.Type.Name);
		}

		return new JsonObject { ["entity"] = entity.Id, ["components"] = visible, ["other"] = other };
	}

	private JsonNode InsertComponents(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		var components = request.GetObject("components") ?? throw RemoteException.InvalidParams("Missing required object parameter 'components'.");
		Insert(world, entity, components);
		return Describe(world, entity, world.TryGet<EntityName>(entity, out var n) ? n.Value : null, Resolve([.. components.Select(static p => p.Key)]));
	}

	private JsonNode MutateComponent(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		var serializer = Resolve([request.GetString("component")])[0];
		if (!serializer.Has(world, entity)) throw RemoteException.NotFound($"Entity {entity.Id} has no {serializer.Name} component.");
		var path = request.GetOptionalString("path") ?? "";
		if (!request.Params!.AsObject().ContainsKey("value")) throw RemoteException.InvalidParams("Missing required parameter 'value'.");
		var value = request.Get("value")?.DeepClone();

		var current = Read(world, entity, serializer);
		JsonNode? updated;
		if (path.Length == 0)
		{
			updated = value;
		}
		else
		{
			updated = current;
			SetPath(updated, path, value, serializer.Name);
		}

		Insert(world, entity, new JsonObject { [serializer.Name] = updated });
		return new JsonObject { ["entity"] = entity.Id, ["component"] = serializer.Name, ["value"] = Read(world, entity, serializer) };
	}

	private JsonNode RemoveComponents(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		var removed = new JsonArray();
		foreach (var serializer in Resolve(request.GetStrings("components")))
		{
			if (!serializer.Has(world, entity)) continue;
			serializer.Remove(world, entity);
			removed.Add((JsonNode)serializer.Name);
		}

		return new JsonObject { ["entity"] = entity.Id, ["removed"] = removed };
	}

	private JsonNode Spawn(RemoteRequest request)
	{
		var world = WorldOf(request);
		var components = request.GetObject("components") ?? [];
		var name = request.GetOptionalString("name");
		// Validate every name before creating anything, so a typo does not leave an empty entity behind.
		Resolve([.. components.Select(static p => p.Key)]);

		var entity = world.Create();
		try
		{
			if (name is not null) world.Add(entity, new EntityName(name));
			Insert(world, entity, components);
		}
		catch
		{
			world.Destroy(entity);
			throw;
		}

		return Describe(world, entity, name, null);
	}

	private JsonNode Despawn(RemoteRequest request)
	{
		var world = WorldOf(request);
		var entity = EntityOf(request, world);
		world.Destroy(entity);
		return new JsonObject { ["entity"] = entity.Id, ["despawned"] = true };
	}

	private JsonObject Describe(World world, Entity entity, string? name, List<ComponentSerializer>? only)
	{
		var components = new JsonObject();
		if (only is null)
		{
			foreach (var serializer in Registry.Items)
			{
				if (serializer.Has(world, entity)) components[serializer.Name] = Read(world, entity, serializer);
			}
		}
		else
		{
			foreach (var serializer in only)
			{
				if (serializer.Has(world, entity)) components[serializer.Name] = Read(world, entity, serializer);
			}
		}

		return new JsonObject { ["entity"] = entity.Id, ["name"] = name, ["components"] = components };
	}

	private static JsonNode? Read(World world, Entity entity, ComponentSerializer serializer)
	{
		var buffer = new ArrayBufferWriter<byte>(128);
		var previous = EntityJson.Write;
		EntityJson.Write = EntityWriteMap.Ids;
		try
		{
			using (var writer = new Utf8JsonWriter(buffer)) serializer.WriteJson(writer, world, entity);
		}
		finally
		{
			EntityJson.Write = previous;
		}

		return JsonNode.Parse(buffer.WrittenSpan);
	}

	private void Insert(World world, Entity entity, JsonObject components)
	{
		var previous = EntityJson.Read;
		EntityJson.Read = new EntityReadMap(world);
		try
		{
			foreach (var (componentName, value) in components)
			{
				var serializer = Resolve([componentName])[0];
				using var document = JsonDocument.Parse(value?.ToJsonString() ?? "null");
				try
				{
					serializer.Insert(document.RootElement, world, entity);
				}
				catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or NotSupportedException)
				{
					throw RemoteException.InvalidParams($"Invalid {componentName} value: {ex.Message}");
				}
			}
		}
		finally
		{
			EntityJson.Read = previous;
		}
	}

	private List<ComponentSerializer> Resolve(IReadOnlyList<string> names)
	{
		var result = new List<ComponentSerializer>(names.Count);
		foreach (var name in names)
		{
			result.Add(Registry.Find(name) ?? throw RemoteException.NotFound($"'{name}' is not a remote-visible component. Remote-visible components: {string.Join(", ", Registry.Names)} (register more with AddEcsSerialization)."));
		}

		return result;
	}

	private static void SetPath(JsonNode? root, string path, JsonNode? value, string component)
	{
		var segments = path.Split('.');
		var node = root;
		for (var i = 0; i < segments.Length; i++)
		{
			var segment = segments[i];
			var last = i == segments.Length - 1;
			switch (node)
			{
				case JsonObject obj:
				{
					var key = obj.ContainsKey(segment) ? segment : obj.Select(static p => p.Key).FirstOrDefault(k => string.Equals(k, segment, StringComparison.OrdinalIgnoreCase))
						?? throw RemoteException.NotFound($"{component} has no field '{segment}' (path '{path}').");
					if (last) obj[key] = value;
					else node = obj[key];
					break;
				}
				case JsonArray array when int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < array.Count:
					if (last) array[index] = value;
					else node = array[index];
					break;
				default:
					throw RemoteException.NotFound($"Path '{path}' does not exist in {component} (at '{segment}').");
			}
		}
	}

	private static bool NameMatches(string? name, string pattern)
	{
		if (name is null) return false;
		return pattern.EndsWith('*') ? name.StartsWith(pattern[..^1], StringComparison.Ordinal) : name == pattern;
	}

	private static IReadOnlyList<World> Worlds(RemoteRequest request)
	{
		var worlds = request.Services.GetService<EcsWorlds>() ?? throw new RemoteException(RemoteErrorCodes.Unsupported, "The game has no ECS module (AddEcs).");
		// Touch the root world so a game that only uses the root has one to inspect.
		_ = worlds.Root;
		return worlds.Worlds;
	}

	private static World WorldOf(RemoteRequest request)
	{
		var worlds = Worlds(request);
		if (worlds.Count == 0) throw new RemoteException(RemoteErrorCodes.Unsupported, "No ECS world is live.");
		var index = request.GetInt32("world", worlds.Count - 1, 0, worlds.Count - 1);
		return worlds[index];
	}

	private static Entity EntityOf(RemoteRequest request, World world)
	{
		var node = request.Get("entity") ?? throw RemoteException.InvalidParams("Missing required parameter 'entity' (an entity id or an EntityName).");
		Entity entity;
		string description;
		if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
		{
			var name = value.GetValue<string>();
			entity = EcsEntities.FindByName(world, name);
			description = $"named '{name}'";
		}
		else if (node is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue<int>(out var id))
		{
			entity = EcsEntities.FindById(world, id);
			description = $"with id {id}";
		}
		else if (node is JsonObject obj && obj["entity"] is JsonValue nested && nested.TryGetValue<int>(out var nestedId))
		{
			entity = EcsEntities.FindById(world, nestedId);
			description = $"with id {nestedId}";
		}
		else
		{
			throw RemoteException.InvalidParams("'entity' must be an entity id (number) or an EntityName (string).");
		}

		if (entity == Entity.Null) throw RemoteException.NotFound($"No live entity {description} in this world.");
		return entity;
	}
}
