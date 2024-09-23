using Benchmark.Core;
using Benchmark.Core.Components;
using Benchmark.Core.Random;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using StableHash32 = Benchmark.Core.Hash.StableHash32;

namespace Benchmark.Entities
{

public partial class ContextEntitiesSystemAPI : ContextBase
{
	private BenchmarkSystemGroup? _group;
	private World?                _world;

	public ContextEntitiesSystemAPI()
		: base("Entities SystemAPI") {}

	protected override void DoSetup()
	{
		var world = _world = new World("World", WorldFlags.None);
		world.EntityManager.CreateSingleton(
			new FramebufferSingleton
			{
				Framebuffer = Framebuffer,
			});
		var group = world.CreateSystemManaged<BenchmarkSystemGroup>();
		group.AddSystemToUpdateList(world.CreateSystem<SpawnSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<RespawnSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<KillSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<RenderSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<SpawnSpriteSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<DeadSpriteSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<UnitsSpriteSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<DamageSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<AttackSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<MovementSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<UpdateVelocitySystem>());
		group.AddSystemToUpdateList(world.CreateSystem<UpdateDataSystem>());
		_group = group;

		for (var n = 0; n < EntityCount; ++n)
		{
			var entity = world.EntityManager.CreateEntity();
			world.EntityManager.AddComponent<TagSpawn>(entity);
			world.EntityManager.AddComponent<CompData>(entity);
			world.EntityManager.AddComponentData<CompUnit>(
				entity,
				new Unit
				{
					Id   = (uint) n,
					Seed = (uint) n,
				});
		}
	}

	protected override void DoRun(int tick) =>
		_group?.Update();

	protected override void DoCleanup()
	{
		_world?.Dispose();
		_world = null;
	}

	private partial struct SpawnSystem : ISystem
	{
		private EntityQuery     _query;
		private EntityArchetype _npcUnitArchetype;
		private EntityArchetype _heroUnitArchetype;
		private EntityArchetype _monsterUnitArchetype;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompUnit>()
																	   .WithAll<TagSpawn>()
																	   .WithOptions(EntityQueryOptions.FilterWriteGroup)
																	   .Build(ref state);
			_npcUnitArchetype = state.EntityManager.CreateArchetype(
				typeof(CompUnit),
				typeof(CompData),
				typeof(CompHealth),
				typeof(CompDamage),
				typeof(CompSprite),
				typeof(CompPosition),
				typeof(CompVelocity),
				typeof(TagNPC));
			_heroUnitArchetype = state.EntityManager.CreateArchetype(
				typeof(CompUnit),
				typeof(CompData),
				typeof(CompHealth),
				typeof(CompDamage),
				typeof(CompSprite),
				typeof(CompPosition),
				typeof(CompVelocity),
				typeof(TagHero));
			_monsterUnitArchetype = state.EntityManager.CreateArchetype(
				typeof(CompUnit),
				typeof(CompData),
				typeof(CompHealth),
				typeof(CompDamage),
				typeof(CompSprite),
				typeof(CompPosition),
				typeof(CompVelocity),
				typeof(TagMonster));
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var entities = _query.ToEntityArray(state.WorldUpdateAllocator);
			foreach (var entity in entities)
			{
				var unit = state.EntityManager.GetComponentData<CompUnit>(entity);
				var data = state.EntityManager.GetComponentData<CompData>(entity);
				switch (SpawnUnit(
							in data.V,
							ref unit.V,
							out var health,
							out var damage,
							out var sprite,
							out var position,
							out var velocity))
				{
				case UnitType.NPC:
					state.EntityManager.SetArchetype(entity, _npcUnitArchetype);
					break;
				case UnitType.Hero:
					state.EntityManager.SetArchetype(entity, _heroUnitArchetype);
					break;
				case UnitType.Monster:
					state.EntityManager.SetArchetype(entity, _monsterUnitArchetype);
					break;
				}

				state.EntityManager.SetComponentData(entity, unit);
				state.EntityManager.SetComponentData<CompHealth>(entity, health);
				state.EntityManager.SetComponentData<CompDamage>(entity, damage);
				state.EntityManager.SetComponentData<CompSprite>(entity, sprite);
				state.EntityManager.SetComponentData<CompPosition>(entity, position);
				state.EntityManager.SetComponentData<CompVelocity>(entity, velocity);
			}
		}
	}

	private partial struct UpdateDataSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var data in SystemAPI.Query<RefRW<CompData>>())
				UpdateDataSystemForEach(ref data.ValueRW.V);
		}
	}

	private partial struct UpdateVelocitySystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var (velocity, unit, data, position) in SystemAPI
															.Query<RefRW<CompVelocity>, RefRW<CompUnit>, RefRO<CompData>
															   , RefRO<CompPosition>>()
															.WithNone<TagDead>())
				UpdateVelocitySystemForEach(
					ref velocity.ValueRW.V,
					ref unit.ValueRW.V,
					in data.ValueRO.V,
					in position.ValueRO.V);
		}
	}

	private partial struct MovementSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var (position, velocity) in SystemAPI.Query<RefRW<CompPosition>, RefRO<CompVelocity>>()
														  .WithNone<TagDead>())
				MovementSystemForEach(ref position.ValueRW.V, in velocity.ValueRO.V);
		}
	}

	private partial struct AttackSystem : ISystem
	{
		private Entity _attackPrefab;

		public void OnCreate(ref SystemState state) =>
			_attackPrefab = state.EntityManager.CreateEntity(typeof(AttackEntity), typeof(Prefab));

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var targets = new NativeList<Target>(state.WorldUpdateAllocator);
			foreach (var (unit, position, entity) in SystemAPI.Query<RefRO<CompUnit>, RefRO<CompPosition>>()
															  .WithNone<TagSpawn, TagDead>()
															  .WithEntityAccess())
				targets.Add(
					new Target
					{
						Id           = unit.ValueRO.V.Id,
						TargetEntity = new TargetEntity(entity, position.ValueRO.V),
					});

			targets.Sort(new TargetComparer());
			var length = targets.Length;
			var ecb    = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var (unitRef, dataRef, damageRef, positionRef) in SystemAPI
																	  .Query<RefRW<CompUnit>, RefRO<CompData>,
																		   RefRO<CompDamage>, RefRO<CompPosition>>()
																	  .WithNone<TagSpawn, TagDead>())
			{
				ref readonly var damage = ref damageRef.ValueRO.V;
				if (damage.Cooldown <= 0)
					continue;

				ref var          unit = ref unitRef.ValueRW.V;
				ref readonly var data = ref dataRef.ValueRO.V;
				var              tick = data.Tick - unit.SpawnTick;
				if (tick % damage.Cooldown != 0)
					continue;

				ref readonly var position     = ref positionRef.ValueRO.V;
				var              generator    = new RandomGenerator(unit.Seed);
				var              index        = generator.Random(ref unit.Counter, length);
				var              target       = targets[index].TargetEntity;
				var              attackEntity = ecb.Instantiate(_attackPrefab);
				ecb.SetComponent(
					attackEntity,
					new AttackEntity
					{
						Target = target.Entity,
						Damage = damage.Attack,
						Ticks  = Common.AttackTicks(position.V, target.Position),
					});
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private partial struct DamageSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var (attackRef, entity) in SystemAPI.Query<RefRW<AttackEntity>>()
														 .WithEntityAccess())
			{
				ref var attack = ref attackRef.ValueRW;
				if (attack.Ticks-- > 0)
					continue;

				var target       = attack.Target;
				var attackDamage = attack.Damage;

				ecb.DestroyEntity(entity);

				if (!SystemAPI.Exists(target)
				 || SystemAPI.HasComponent<TagDead>(target))
					continue;

				ref var health = ref SystemAPI.GetComponentRW<CompHealth>(target)
											  .ValueRW.V;
				ref readonly var damage = ref SystemAPI.GetComponentRO<CompDamage>(target)
													   .ValueRO.V;
				ApplyDamageSequential(ref health, in damage, in attack);
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private partial struct KillSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var (unitRef, health, data, entity) in SystemAPI
														   .Query<RefRW<CompUnit>, RefRO<CompHealth>, RefRO<CompData>>()
														   .WithNone<TagDead>()
														   .WithEntityAccess())
			{
				if (health.ValueRO.V.Hp > 0)
					continue;

				ref var unit = ref unitRef.ValueRW.V;
				ecb.AddComponent<TagDead>(entity);
				unit.RespawnTick = data.ValueRO.V.Tick + RespawnTicks;
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private struct FramebufferSingleton : IComponentData
	{
		public Framebuffer Framebuffer;
	}

	private partial struct RenderSystem : ISystem
	{
		public void OnCreate(ref SystemState state) =>
			state.RequireForUpdate<FramebufferSingleton>();

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var framebuffer = SystemAPI.GetSingleton<FramebufferSingleton>()
									   .Framebuffer;
			foreach (var (position, sprite, unit, data) in SystemAPI
						.Query<RefRO<CompPosition>, RefRO<CompSprite>, RefRO<CompUnit>, RefRO<CompData>>())
				RenderSystemForEach(
					framebuffer,
					in position.ValueRO.V,
					in sprite.ValueRO.V,
					in unit.ValueRO.V,
					in data.ValueRO.V);
		}
	}

	private partial struct SpawnSpriteSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var spite in SystemAPI.Query<RefRW<CompSprite>>()
										   .WithAll<TagSpawn>())
				spite.ValueRW.V.Character = SpriteMask.Spawn;
		}
	}

	private partial struct DeadSpriteSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var spite in SystemAPI.Query<RefRW<CompSprite>>()
										   .WithAll<TagDead>())
				spite.ValueRW.V.Character = SpriteMask.Grave;
		}
	}

	private partial struct UnitsSpriteSystem : ISystem
	{
		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			foreach (var spite in SystemAPI.Query<RefRW<CompSprite>>()
										   .WithAll<TagNPC>()
										   .WithNone<TagSpawn>()
										   .WithNone<TagDead>())
				spite.ValueRW.V.Character = SpriteMask.NPC;
			foreach (var spite in SystemAPI.Query<RefRW<CompSprite>>()
										   .WithAll<TagHero>()
										   .WithNone<TagSpawn>()
										   .WithNone<TagDead>())
				spite.ValueRW.V.Character = SpriteMask.Hero;
			foreach (var spite in SystemAPI.Query<RefRW<CompSprite>>()
										   .WithAll<TagMonster>()
										   .WithNone<TagSpawn>()
										   .WithNone<TagDead>())
				spite.ValueRW.V.Character = SpriteMask.Monster;
		}
	}

	private partial struct RespawnSystem : ISystem
	{
		private Entity _spawnPrefab;

		public void OnCreate(ref SystemState state) =>
			_spawnPrefab = state.EntityManager.CreateEntity(
				typeof(CompUnit),
				typeof(CompData),
				typeof(TagSpawn),
				typeof(Prefab));

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var (unitRef, dataRef, entity) in SystemAPI.Query<RefRO<CompUnit>, RefRO<CompData>>()
																.WithAll<TagDead>()
																.WithEntityAccess())
			{
				ref readonly var unit = ref unitRef.ValueRO.V;
				ref readonly var data = ref dataRef.ValueRO.V;
				if (data.Tick < unit.RespawnTick)
					continue;

				var newEntity = ecb.Instantiate(_spawnPrefab);
				ecb.SetComponent<CompData>(newEntity, data);
				ecb.SetComponent<CompUnit>(
					newEntity,
					new Unit
					{
						Id   = unit.Id | (uint) data.Tick << 16,
						Seed = StableHash32.Hash(unit.Seed, unit.Counter),
					});

				ecb.DestroyEntity(entity);
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private partial class BenchmarkSystemGroup : ComponentSystemGroup
	{
		public BenchmarkSystemGroup() =>
			EnableSystemSorting = false;
	}
}

}
