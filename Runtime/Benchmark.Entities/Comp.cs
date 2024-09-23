using System.Runtime.CompilerServices;
using Benchmark.Core.Components;
using Unity.Entities;
using Unity.Mathematics;

namespace Benchmark.Entities
{

public struct CompPosition : IComponentData
{
	public Position V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompPosition(Position value) =>
		new() { V = value };
}

public struct CompVelocity : IComponentData
{
	public Velocity V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompVelocity(Velocity value) =>
		new() { V = value };
}

public struct CompSprite : IComponentData
{
	public Sprite V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompSprite(Sprite value) =>
		new() { V = value };
}

public struct CompUnit : IComponentData
{
	public Unit V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompUnit(Unit value) =>
		new() { V = value };
}

public struct CompData : IComponentData
{
	public Data V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompData(Data value) =>
		new() { V = value };
}

public struct CompHealth : IComponentData
{
	public Health V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompHealth(Health value) =>
		new() { V = value };
}

public struct CompDamage : IComponentData
{
	public Damage V;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator CompDamage(Damage value) =>
		new() { V = value };
}

public struct AttackEntity
	: IComponentData,
	  IAttack
{
	public Entity Target;
	public int    Damage;
	public int    Ticks;

	int IAttack.Damage
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => Damage;
	}
}

public struct TargetEntity
{
	public Entity Entity;
	public float2 Position;

	public TargetEntity(Entity entity, Position position)
	{
		Entity   = entity;
		Position = position.V;
	}
}

}
