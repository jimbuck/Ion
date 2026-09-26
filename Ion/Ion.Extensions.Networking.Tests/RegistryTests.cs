using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, UNIT)]
public class RegistryTests
{
	private sealed class Dummy : NetSerializer<int>
	{
		public override int MaxSize => 4;
		public override void Write(ref NetWriter writer, in int value) => writer.WriteInt32(value);
		public override int Read(ref NetReader reader) => reader.ReadInt32();
		public override bool WriteDelta(ref NetWriter writer, in int baseline, in int value) => false;
		public override int ReadDelta(ref NetReader reader, in int baseline) => baseline;
		public override bool Equal(in int a, in int b) => a == b;
	}

	private static NetworkTypeInfo Component(string name, string layout = "Value:int", Authority authority = Authority.Server) =>
		new ReplicatedTypeInfo<int>(name, layout, new Dummy(), authority);

	private static NetworkTypeInfo Message(string name, string layout = "Value:int", Delivery delivery = Delivery.ReliableOrdered) =>
		new MessageTypeInfo<int>(name, layout, new Dummy(), delivery);

	[Fact]
	public void TheHashDoesNotDependOnRegistrationOrder()
	{
		NetworkTypeInfo[] types = [Component("B.Health"), Component("A.Position"), Message("Chat"), Message("Input")];
		var forward = NetworkTypeTable.Create(types);
		var backward = NetworkTypeTable.Create(types.Reverse());
		Assert.Equal(forward.Hash, backward.Hash);
		Assert.Equal("A.Position", forward.Components[0].Name);
		Assert.Equal("B.Health", forward.Components[1].Name);
	}

	[Fact]
	public void TheHashIsStableAcrossBuilds()
	{
		// Pinned: the hash is what two builds of a game compare in the handshake, so it must not depend on anything but
		// the type signatures (not on string hashing, which .NET randomizes per process, nor on the runtime).
		NetworkTypeInfo[] types = [Component("Game.Health", "Current:int;Max:int"), Message("Game.Chat", "Text:Ion.Extensions.Networking.FixedString64")];
		Assert.Equal(0xB4308183BF81C42CUL, NetworkTypeTable.Create(types).Hash);
		Assert.Equal(0xCBF29CE484222325UL, NetworkTypeTable.Fnv1a64(""));
		Assert.Equal(0xAF63DC4C8601EC8CUL, NetworkTypeTable.Fnv1a64("a"));
	}

	[Fact]
	public void AnyChangeOfASignatureChangesTheHash()
	{
		var baseline = NetworkTypeTable.Create([Component("Health"), Message("Chat")]).Hash;
		Assert.NotEqual(baseline, NetworkTypeTable.Create([Component("Health", "Value:long"), Message("Chat")]).Hash);
		Assert.NotEqual(baseline, NetworkTypeTable.Create([Component("Health", authority: Authority.Owner), Message("Chat")]).Hash);
		Assert.NotEqual(baseline, NetworkTypeTable.Create([Component("Health"), Message("Chat", delivery: Delivery.Unreliable)]).Hash);
		Assert.NotEqual(baseline, NetworkTypeTable.Create([Component("Health")]).Hash);
		Assert.NotEqual(baseline, NetworkTypeTable.Create([Component("Health2"), Message("Chat")]).Hash);
	}

	[Fact]
	public void TheProcessTableNumbersEachKindByName()
	{
		var table = NetworkRegistry.Table;
		for (var i = 1; i < table.Components.Count; i++) Assert.True(string.CompareOrdinal(table.Components[i - 1].Name, table.Components[i].Name) < 0);
		for (var i = 0; i < table.Components.Count; i++) Assert.Equal(i, table.Components[i].Ordinal);
		for (var i = 0; i < table.Messages.Count; i++) Assert.Equal(i, table.Messages[i].Ordinal);
		Assert.Same(NetworkTypes<Health>.Component, table.Component((uint)NetworkTypes<Health>.Component!.Ordinal));
		Assert.Null(table.Message(10_000));
	}
}
