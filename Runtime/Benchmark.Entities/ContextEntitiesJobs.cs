using Benchmark.Core;
using Benchmark.Core.Components;
using Benchmark.Core.Random;
using Benchmark.Entities;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using StableHash32 = Benchmark.Core.Hash.StableHash32;

[assembly: RegisterGenericJobType(typeof(SortJobDefer<Target, TargetComparer>.SegmentSort))]
[assembly: RegisterGenericJobType(typeof(SortJobDefer<Target, TargetComparer>.SegmentSortMerge))]

namespace Benchmark.Entities
{

public partial class ContextEntitiesJobs : ContextBase
{
	private BenchmarkSystemGroup? _group;
	private World?                _world;

	public ContextEntitiesJobs()
		: base("Entities Jobs") {}

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
		group.AddSystemToUpdateList(world.CreateSystem<SpriteSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<DamageSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<AttackSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<MovementSystem>());
		group.AddSystemToUpdateList(world.CreateSystem<UpdateVelocitySystem>());
		group.AddSystemToUpdateList(world.CreateSystem<UpdateDataSystem>());
		group.AddSystemToUpdateList(world.CreateSystemManaged<BenchmarkEntityCommandBufferSystem>());
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
																	   .WithAll<CompData, TagSpawn>()
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
		private partial struct Job : IJobEntity
		{
			private void Execute(ref CompData data) =>
				UpdateDataSystemForEach(ref data.V);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state) =>
			new Job().Schedule();
	}

	private partial struct UpdateVelocitySystem : ISystem
	{
		[BurstCompile]
		[WithNone(typeof(TagDead))]
		private partial struct Job : IJobEntity
		{
			private void Execute(
				ref CompVelocity velocity,
				ref CompUnit unit,
				in CompData data,
				in CompPosition position) =>
				UpdateVelocitySystemForEach(
					ref velocity.V,
					ref unit.V,
					in data.V,
					in position.V);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state) =>
			new Job().Schedule();
	}

	private partial struct MovementSystem : ISystem
	{
		[BurstCompile]
		[WithNone(typeof(TagDead))]
		private partial struct Job : IJobEntity
		{
			private void Execute(ref CompPosition position, ref CompVelocity velocity) =>
				MovementSystemForEach(ref position.V, in velocity.V);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state) =>
			new Job().Schedule();
	}

	private partial struct AttackSystem : ISystem
	{
		[BurstCompile]
		[WithNone(typeof(TagDead), typeof(TagSpawn))]
		private partial struct FillTargetsJob : IJobEntity
		{
			public NativeList<Target> Targets;

			private void Execute(Entity entity, in CompUnit unit, in CompPosition position) =>
				Targets.Add(
					new Target
					{
						Id           = unit.V.Id,
						TargetEntity = new TargetEntity(entity, position.V),
					});
		}

		[BurstCompile]
		[WithNone(typeof(TagDead), typeof(TagSpawn))]
		private partial struct AttacksJob : IJobEntity
		{
			public Entity AttackPrefab;

			[ReadOnly]
			public NativeList<Target> Targets;

			public EntityCommandBuffer Ecb;

			private void Execute(
				ref CompUnit unit,
				in CompData data,
				in CompDamage damage,
				in CompPosition position)
			{
				if (damage.V.Cooldown <= 0)
					return;

				var tick = data.V.Tick - unit.V.SpawnTick;
				if (tick % damage.V.Cooldown != 0)
					return;

				var generator    = new RandomGenerator(unit.V.Seed);
				var count        = Targets.Length;
				var index        = generator.Random(ref unit.V.Counter, count);
				var target       = Targets[index].TargetEntity;
				var attackEntity = Ecb.Instantiate(AttackPrefab);
				Ecb.SetComponent(
					attackEntity,
					new AttackEntity
					{
						Target = target.Entity,
						Damage = damage.V.Attack,
						Ticks  = Common.AttackTicks(position.V.V, target.Position),
					});
			}
		}

		private Entity _attackPrefab;

		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<BenchmarkEntityCommandBufferSystem.Singleton>();
			_attackPrefab = state.EntityManager.CreateEntity(typeof(AttackEntity), typeof(Prefab));
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var targets = new NativeList<Target>(state.WorldUpdateAllocator);
			state.Dependency = new FillTargetsJob
			{
				Targets = targets,
			}.Schedule(state.Dependency);
			state.Dependency = targets.SortJobDefer(new TargetComparer())
									  .Schedule(state.Dependency);
			state.Dependency = new AttacksJob
			{
				AttackPrefab = _attackPrefab,
				Targets      = targets,
				Ecb = SystemAPI.GetSingleton<BenchmarkEntityCommandBufferSystem.Singleton>()
							   .CreateCommandBuffer(state.WorldUnmanaged),
			}.Schedule(state.Dependency);
			state.Dependency = targets.Dispose(state.Dependency);
		}
	}

	private partial struct DamageSystem : ISystem
	{
		[BurstCompile]
		private partial struct Job : IJobEntity
		{
			[ReadOnly]
			public EntityStorageInfoLookup EntityLookup;

			[ReadOnly]
			public ComponentLookup<TagDead> DeadLookup;

			public ComponentLookup<CompHealth> HealthLookup;

			[ReadOnly]
			public ComponentLookup<CompDamage> DamageLookup;

			public EntityCommandBuffer Ecb;

			private void Execute(ref AttackEntity attack, Entity entity)
			{
				if (attack.Ticks-- > 0)
					return;

				var target       = attack.Target;
				var attackDamage = attack.Damage;

				Ecb.DestroyEntity(entity);

				if (!EntityLookup.Exists(target)
				 || DeadLookup.HasComponent(target))
					return;

				ref var health = ref HealthLookup.GetRefRW(target)
												 .ValueRW.V;
				ref readonly var damage = ref DamageLookup.GetRefRO(target)
														  .ValueRO.V;
				var totalDamage = attackDamage - damage.Defence;
				health.Hp -= totalDamage;
			}
		}

		public void OnCreate(ref SystemState state) =>
			state.RequireForUpdate<BenchmarkEntityCommandBufferSystem.Singleton>();

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			var ecbSingleton = SystemAPI.GetSingleton<BenchmarkEntityCommandBufferSystem.Singleton>();
			new Job
			{
				EntityLookup = SystemAPI.GetEntityStorageInfoLookup(),
				DeadLookup   = SystemAPI.GetComponentLookup<TagDead>(),
				HealthLookup = SystemAPI.GetComponentLookup<CompHealth>(),
				DamageLookup = SystemAPI.GetComponentLookup<CompDamage>(),
				Ecb          = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged),
			}.Schedule();
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
		[BurstCompile]
		private partial struct RenderJob : IJobEntity
		{
			public Framebuffer Buffer;

			private void Execute(
				in CompPosition position,
				in CompSprite sprite,
				in CompUnit unit,
				in CompData data) =>
				RenderSystemForEach(
					Buffer,
					in position.V,
					in sprite.V,
					in unit.V,
					in data.V);
		}

		public void OnCreate(ref SystemState state) =>
			state.RequireForUpdate<FramebufferSingleton>();

		[BurstCompile]
		public void OnUpdate(ref SystemState state) =>
			new RenderJob
			{
				Buffer = SystemAPI.GetSingleton<FramebufferSingleton>()
								  .Framebuffer,
			}.Schedule();
	}

	private partial struct SpriteSystem : ISystem
	{
		[BurstCompile]
		[WithAll(typeof(TagSpawn))]
		private partial struct SpawnJob : IJobEntity
		{
			private void Execute(ref CompSprite sprite) =>
				sprite.V.Character = SpriteMask.Spawn;
		}

		[BurstCompile]
		[WithAll(typeof(TagDead))]
		private partial struct DeadJob : IJobEntity
		{
			private void Execute(ref CompSprite sprite) =>
				sprite.V.Character = SpriteMask.Grave;
		}

		[BurstCompile]
		[WithAll(typeof(TagNPC))]
		[WithNone(typeof(TagSpawn), typeof(TagDead))]
		private partial struct NPCJob : IJobEntity
		{
			private void Execute(ref CompSprite sprite) =>
				sprite.V.Character = SpriteMask.NPC;
		}

		[BurstCompile]
		[WithAll(typeof(TagHero))]
		[WithNone(typeof(TagSpawn), typeof(TagDead))]
		private partial struct HeroJob : IJobEntity
		{
			private void Execute(ref CompSprite sprite) =>
				sprite.V.Character = SpriteMask.Hero;
		}

		[BurstCompile]
		[WithAll(typeof(TagMonster))]
		[WithNone(typeof(TagSpawn), typeof(TagDead))]
		private partial struct MonsterJob : IJobEntity
		{
			private void Execute(ref CompSprite sprite) =>
				sprite.V.Character = SpriteMask.Monster;
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			state.Dependency = new SpawnJob().Schedule(state.Dependency);
			state.Dependency = new DeadJob().Schedule(state.Dependency);
			state.Dependency = new NPCJob().Schedule(state.Dependency);
			state.Dependency = new HeroJob().Schedule(state.Dependency);
			state.Dependency = new MonsterJob().Schedule(state.Dependency);
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

	private partial class BenchmarkEntityCommandBufferSystem : EntityCommandBufferSystem
	{
		protected override void OnCreate()
		{
			base.OnCreate();
			this.RegisterSingleton<Singleton>(ref PendingBuffers, World.Unmanaged);
		}

		public unsafe struct Singleton
			: IComponentData,
			  IECBSingleton
		{
			private UnsafeList<EntityCommandBuffer>* _pendingBuffers;
			private AllocatorManager.AllocatorHandle _allocator;

			public EntityCommandBuffer CreateCommandBuffer(WorldUnmanaged world) =>
				EntityCommandBufferSystem.CreateCommandBuffer(ref *_pendingBuffers, _allocator, world);

			public void SetPendingBufferList(ref UnsafeList<EntityCommandBuffer> buffers) =>
				_pendingBuffers = (UnsafeList<EntityCommandBuffer>*) UnsafeUtility.AddressOf(ref buffers);

			public void SetAllocator(Allocator allocatorIn) =>
				_allocator = allocatorIn;

			public void SetAllocator(AllocatorManager.AllocatorHandle allocatorIn) =>
				_allocator = allocatorIn;
		}
	}
}

}
