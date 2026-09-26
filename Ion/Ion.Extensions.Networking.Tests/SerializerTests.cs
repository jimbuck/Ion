using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

[Trait(CATEGORY, UNIT)]
public class SerializerTests
{
	private static NetSerializer<T> Serializer<T>() where T : unmanaged =>
		(NetSerializer<T>?)NetworkTypes<T>.Component?.Serializer ?? NetworkTypes<T>.Message!.Serializer;

	private static byte[] Full<T>(in T value) where T : unmanaged
	{
		var buffer = new byte[1024];
		var writer = new NetWriter(buffer);
		Serializer<T>().Write(ref writer, value);
		Assert.False(writer.Overflowed);
		return writer.Written.ToArray();
	}

	private static T ReadFull<T>(byte[] bytes) where T : unmanaged
	{
		var reader = new NetReader(bytes);
		var value = Serializer<T>().Read(ref reader);
		Assert.False(reader.Failed);
		Assert.True(reader.End);
		return value;
	}

	[Fact]
	public void TheGeneratorRegisteredEveryTestType()
	{
		Assert.NotNull(NetworkTypes<Health>.Component);
		Assert.NotNull(NetworkTypes<Position>.Component);
		Assert.True(NetworkTypes<Position>.Component!.Interpolated);
		Assert.True(NetworkTypes<Avatar>.Component!.Predicted);
		Assert.Equal(Authority.Owner, NetworkTypes<Avatar>.Component!.Authority);
		Assert.Equal(Authority.Owner, NetworkTypes<Aim>.Component!.Authority);
		Assert.Equal(MessageDirection.ClientToServer, NetworkTypes<MoveInput>.Message!.Direction);
		Assert.Equal(Delivery.Unreliable, NetworkTypes<MoveInput>.Message!.Delivery);
		Assert.Equal(Delivery.ReliableOrdered, NetworkTypes<Chat>.Message!.Delivery);
		Assert.Equal("Health", NetworkTypes<Health>.Component!.Type.Name);
		Assert.Equal("Current:int;Max:int", NetworkTypes<Health>.Component!.Layout);
	}

	[Fact]
	public void FullFormRoundTripsEveryMemberKind()
	{
		var profile = new Profile("Ada", Team.Blue, new Stats(12, 0.75f), Quaternion.CreateFromYawPitchRoll(1, 2, 3), 99.25, LocalOnly: 42);
		var bytes = Full(profile);
		var read = ReadFull<Profile>(bytes);

		Assert.Equal("Ada", read.Name.ToString());
		Assert.Equal(Team.Blue, read.Team);
		Assert.Equal(new Stats(12, 0.75f), read.Stats);
		Assert.Equal(profile.Facing, read.Facing);
		Assert.Equal(99.25, read.Score);

		// [NetworkIgnore] members are not sent.
		Assert.Equal(0, read.LocalOnly);
		Assert.Equal(1 + 3 + 1 + 4 + 4 + 16 + 8, bytes.Length);
	}

	[Fact]
	public void DeltaWritesOnlyTheChangedMembers()
	{
		var serializer = Serializer<Profile>();
		var baseline = new Profile("Ada", Team.Blue, new Stats(1, 0.5f), Quaternion.Identity, 10, 0);
		var buffer = new byte[256];

		// Unchanged: nothing written.
		var writer = new NetWriter(buffer);
		Assert.False(serializer.WriteDelta(ref writer, baseline, baseline));
		Assert.Equal(0, writer.Position);

		// One member changed: a one-byte mask and the member.
		var changed = baseline with { Score = 11 };
		writer = new NetWriter(buffer);
		Assert.True(serializer.WriteDelta(ref writer, baseline, changed));
		Assert.Equal(1 + 8, writer.Position);

		var reader = new NetReader(writer.Written);
		var read = serializer.ReadDelta(ref reader, baseline);
		Assert.False(reader.Failed);
		Assert.Equal(changed, read);
	}

	[Fact]
	public void FloatsAreComparedByTheirBits()
	{
		var serializer = Serializer<Position>();
		Assert.False(serializer.Equal(new Position(new Vector2(0f, 1)), new Position(new Vector2(-0f, 1))));
		Assert.True(serializer.Equal(new Position(new Vector2(float.NaN, 1)), new Position(new Vector2(float.NaN, 1))));

		var buffer = new byte[64];
		var writer = new NetWriter(buffer);
		Assert.True(serializer.WriteDelta(ref writer, new Position(Vector2.Zero), new Position(new Vector2(-0f, 0))));
		var reader = new NetReader(writer.Written);
		var read = serializer.ReadDelta(ref reader, new Position(Vector2.Zero));
		Assert.True(float.IsNegative(read.Value.X));
	}

	[Fact]
	public void AChainOfDeltasRestoresTheExactValue()
	{
		var serializer = Serializer<Profile>();
		var random = new Random(3);
		var sender = new Profile("x", Team.Red, default, Quaternion.Identity, 0, 0);
		var receiver = sender;
		var buffer = new byte[256];

		for (var i = 0; i < 500; i++)
		{
			var next = sender;
			switch (random.Next(4))
			{
				case 0: next.Score = random.NextDouble(); break;
				case 1: next.Stats = new Stats(random.Next(), random.NextSingle()); break;
				case 2: next.Name = "n" + random.Next(1000); break;
				default: next.Team = next.Team == Team.Red ? Team.Blue : Team.Red; break;
			}

			var writer = new NetWriter(buffer);
			if (serializer.WriteDelta(ref writer, sender, next))
			{
				var reader = new NetReader(writer.Written);
				receiver = serializer.ReadDelta(ref reader, receiver);
				Assert.False(reader.Failed);
			}

			sender = next;
			Assert.True(serializer.Equal(sender, receiver), $"step {i}");
		}
	}

	[Fact]
	public void TruncatedOrCorruptInputFailsWithoutThrowing()
	{
		var bytes = Full(new Profile("Ada", Team.Blue, new Stats(1, 1), Quaternion.Identity, 1, 0));
		for (var length = 0; length < bytes.Length; length++)
		{
			var reader = new NetReader(bytes.AsSpan(0, length));
			Serializer<Profile>().Read(ref reader);
			Assert.True(reader.Failed, $"length {length}");
		}

		// A delta mask with bits beyond the members fails.
		var mask = new byte[] { 0xFF, 0xFF, 0x03 };
		var deltaReader = new NetReader(mask);
		Serializer<Health>().ReadDelta(ref deltaReader, default);
		Assert.True(deltaReader.Failed);
	}

	[Fact]
	public void WritesThatDoNotFitOverflowWithoutThrowing()
	{
		var buffer = new byte[5];
		var writer = new NetWriter(buffer);
		Serializer<Profile>().Write(ref writer, default);
		Assert.True(writer.Overflowed);
	}

	[Fact]
	public void InterpolatedComponentsBlendLinearly()
	{
		var serializer = Serializer<Position>();
		var mid = serializer.Interpolate(new Position(new Vector2(0, 10)), new Position(new Vector2(10, 20)), 0.25f);
		Assert.Equal(new Vector2(2.5f, 12.5f), mid.Value);

		// Not interpolated: switches at the midpoint.
		Assert.Equal(new Health(1, 1), Serializer<Health>().Interpolate(new Health(1, 1), new Health(2, 2), 0.4f));
		Assert.Equal(new Health(2, 2), Serializer<Health>().Interpolate(new Health(1, 1), new Health(2, 2), 0.6f));
	}

	[Fact]
	public void FixedStringsTruncateToWholeCharactersAndRoundTrip()
	{
		var text = new string('é', 40);
		FixedString32 value = text;
		Assert.True(value.Length <= FixedString32.Capacity);
		Assert.Equal(new string('é', 15), value.ToString());
		var message = new Chat(3, "hello, world");
		Assert.Equal(message, ReadFull<Chat>(Full(message)));
	}

	[Fact]
	public void VarIntsRoundTrip()
	{
		var buffer = new byte[64];
		var writer = new NetWriter(buffer);
		uint[] values = [0, 1, 127, 128, 16383, 16384, uint.MaxValue];
		foreach (var v in values) writer.WriteVarUInt32(v);
		writer.WriteVarInt32(-1);
		writer.WriteVarInt32(int.MinValue);
		writer.WriteVarUInt64(ulong.MaxValue);
		var reader = new NetReader(writer.Written);
		foreach (var v in values) Assert.Equal(v, reader.ReadVarUInt32());
		Assert.Equal(-1, reader.ReadVarInt32());
		Assert.Equal(int.MinValue, reader.ReadVarInt32());
		Assert.Equal(ulong.MaxValue, reader.ReadVarUInt64());
		Assert.True(reader.End);
		Assert.False(reader.Failed);
	}
}
