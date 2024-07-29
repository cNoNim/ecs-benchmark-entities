using Benchmark.Core;
using Benchmark.Core.Components;
using Benchmark.Core.Random;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using StableHash32 = Benchmark.Core.Hash.StableHash32;

namespace Benchmark.Entities
{

[BurstCompile]
public partial class ContextEntities : ContextBase
{
	private BenchmarkSystemGroup? _group;
	private World?                _world;

	public ContextEntities()
		: base("Entities") {}

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
																	   .WithAll<CompData, TagSpawn>()
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
		private EntityQuery                   _query;
		private ComponentTypeHandle<CompData> _dataHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompData>()
																	   .Build(ref state);
			_dataHandle = state.GetComponentTypeHandle<CompData>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_dataHandle.Update(ref state);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var datas = chunk.GetNativeArray(ref _dataHandle);
				for (var n = 0; n < datas.Length; n++)
				{
					var data = datas[n];
					UpdateDataSystemForEach(ref data.V);
					datas[n] = data;
				}
			}
		}
	}

	private partial struct UpdateVelocitySystem : ISystem
	{
		private EntityQuery                       _query;
		private ComponentTypeHandle<CompVelocity> _velocityHandle;
		private ComponentTypeHandle<CompUnit>     _unitHandle;
		private ComponentTypeHandle<CompData>     _dataHandle;
		private ComponentTypeHandle<CompPosition> _positionHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompVelocity, CompUnit>()
																	   .WithAll<CompData, CompPosition>()
																	   .WithNone<TagDead>()
																	   .Build(ref state);

			_velocityHandle = state.GetComponentTypeHandle<CompVelocity>();
			_unitHandle     = state.GetComponentTypeHandle<CompUnit>();
			_dataHandle     = state.GetComponentTypeHandle<CompData>(true);
			_positionHandle = state.GetComponentTypeHandle<CompPosition>(true);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_velocityHandle.Update(ref state);
			_unitHandle.Update(ref state);
			_dataHandle.Update(ref state);
			_positionHandle.Update(ref state);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var velocities = chunk.GetNativeArray(ref _velocityHandle);
				var units      = chunk.GetNativeArray(ref _unitHandle);
				var datas      = chunk.GetNativeArray(ref _dataHandle);
				var positions  = chunk.GetNativeArray(ref _positionHandle);
				for (var n = 0; n < units.Length; n++)
				{
					var velocity = velocities[n];
					var unit     = units[n];
					var data     = datas[n];
					var position = positions[n];
					UpdateVelocitySystemForEach(
						ref velocity.V,
						ref unit.V,
						in data.V,
						in position.V);
					velocities[n] = velocity;
					units[n]      = unit;
				}
			}
		}
	}

	private partial struct MovementSystem : ISystem
	{
		private EntityQuery                       _query;
		private ComponentTypeHandle<CompPosition> _positionHandle;
		private ComponentTypeHandle<CompVelocity> _velocityHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompPosition>()
																	   .WithAll<CompVelocity>()
																	   .WithNone<TagDead>()
																	   .Build(ref state);
			_positionHandle = state.GetComponentTypeHandle<CompPosition>();
			_velocityHandle = state.GetComponentTypeHandle<CompVelocity>(true);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_positionHandle.Update(ref state);
			_velocityHandle.Update(ref state);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var positions  = chunk.GetNativeArray(ref _positionHandle);
				var velocities = chunk.GetNativeArray(ref _velocityHandle);
				for (var n = 0; n < positions.Length; n++)
				{
					var position = positions[n];
					var velocity = velocities[n];
					MovementSystemForEach(ref position.V, in velocity.V);
					positions[n] = position;
				}
			}
		}
	}

	private partial struct AttackSystem : ISystem
	{
		private Entity                            _attackPrefab;
		private EntityQuery                       _query;
		private EntityTypeHandle                  _entityHandle;
		private ComponentTypeHandle<CompUnit>     _unitHandle;
		private ComponentTypeHandle<CompData>     _dataHandle;
		private ComponentTypeHandle<CompDamage>   _damageHandle;
		private ComponentTypeHandle<CompPosition> _positionHandle;

		public void OnCreate(ref SystemState state)
		{
			_attackPrefab = state.EntityManager.CreateEntity(typeof(AttackEntity), typeof(Prefab));
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompUnit>()
																	   .WithAll<CompData, CompDamage, CompPosition>()
																	   .WithNone<TagSpawn, TagDead>()
																	   .Build(ref state);
			_entityHandle   = state.GetEntityTypeHandle();
			_unitHandle     = state.GetComponentTypeHandle<CompUnit>();
			_dataHandle     = state.GetComponentTypeHandle<CompData>(true);
			_damageHandle   = state.GetComponentTypeHandle<CompDamage>(true);
			_positionHandle = state.GetComponentTypeHandle<CompPosition>(true);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_entityHandle.Update(ref state);
			_unitHandle.Update(ref state);
			_dataHandle.Update(ref state);
			_damageHandle.Update(ref state);
			_positionHandle.Update(ref state);
			var targets = new NativeList<Target>(state.WorldUpdateAllocator);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var entities  = chunk.GetNativeArray(_entityHandle);
				var units     = chunk.GetNativeArray(ref _unitHandle);
				var positions = chunk.GetNativeArray(ref _positionHandle);
				for (var n = 0; n < units.Length; n++)
					targets.Add(
						new Target
						{
							Id           = units[n].V.Id,
							TargetEntity = new TargetEntity(entities[n], positions[n].V),
						});
			}

			targets.Sort(new TargetComparer());

			var length = targets.Length;
			var ecb    = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var units     = chunk.GetNativeArray(ref _unitHandle);
				var damages   = chunk.GetNativeArray(ref _damageHandle);
				var datas     = chunk.GetNativeArray(ref _dataHandle);
				var positions = chunk.GetNativeArray(ref _positionHandle);
				for (var n = 0; n < units.Length; n++)
				{
					var damage = damages[n].V;

					if (damage.Cooldown <= 0)
						continue;

					var unit = units[n].V;
					var tick = datas[n].V.Tick - unit.SpawnTick;
					if (tick % damage.Cooldown != 0)
						continue;

					var generator    = new RandomGenerator(unit.Seed);
					var index        = generator.Random(ref unit.Counter, length);
					var target       = targets[index].TargetEntity;
					var attackEntity = ecb.Instantiate(_attackPrefab);
					ecb.SetComponent(
						attackEntity,
						new AttackEntity
						{
							Target = target.Entity,
							Damage = damage.Attack,
							Ticks  = Common.AttackTicks(positions[n].V.V, target.Position),
						});
					units[n] = unit;
				}
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private partial struct DamageSystem : ISystem
	{
		private EntityQuery                       _attackQuery;
		private EntityTypeHandle                  _entityHandle;
		private EntityStorageInfoLookup           _entityLookup;
		private ComponentTypeHandle<AttackEntity> _attackHandle;
		private ComponentLookup<TagDead>          _deadLookup;
		private ComponentLookup<CompHealth>       _healthLookup;
		private ComponentLookup<CompDamage>       _damageLookup;

		public void OnCreate(ref SystemState state)
		{
			_attackQuery = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<AttackEntity>()
																			 .Build(ref state);
			_entityHandle = state.GetEntityTypeHandle();
			_entityLookup = state.GetEntityStorageInfoLookup();
			_attackHandle = state.GetComponentTypeHandle<AttackEntity>();
			_deadLookup   = state.GetComponentLookup<TagDead>();
			_healthLookup = state.GetComponentLookup<CompHealth>();
			_damageLookup = state.GetComponentLookup<CompDamage>(true);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_entityHandle.Update(ref state);
			_entityLookup.Update(ref state);
			_attackHandle.Update(ref state);
			_deadLookup.Update(ref state);
			_healthLookup.Update(ref state);
			_damageLookup.Update(ref state);
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var chunk in _attackQuery.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var entities = chunk.GetNativeArray(_entityHandle);
				var attacks  = chunk.GetNativeArray(ref _attackHandle);
				for (var n = 0; n < entities.Length; n++)
				{
					var entity = entities[n];
					var attack = attacks[n];
					if (attack.Ticks-- > 0)
					{
						attacks[n] = attack;
						continue;
					}

					var target       = attack.Target;
					var attackDamage = attack.Damage;

					ecb.DestroyEntity(entity);

					if (!_entityLookup.Exists(target)
					 || _deadLookup.HasComponent(target))
						continue;

					var health      = _healthLookup[target].V;
					var damage      = _damageLookup[target].V;
					var totalDamage = attackDamage - damage.Defence;
					health.Hp             -= totalDamage;
					_healthLookup[target] =  health;
				}
			}

			ecb.Playback(state.EntityManager);
		}
	}

	private partial struct KillSystem : ISystem
	{
		private EntityQuery                     _query;
		private ComponentTypeHandle<CompUnit>   _unitHandle;
		private ComponentTypeHandle<CompHealth> _healthHandle;
		private ComponentTypeHandle<CompData>   _dataHandle;
		private EntityTypeHandle                _entityHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompUnit>()
																	   .WithAll<CompHealth, CompData>()
																	   .WithNone<TagDead>()
																	   .Build(ref state);
			_entityHandle = state.GetEntityTypeHandle();
			_unitHandle   = state.GetComponentTypeHandle<CompUnit>();
			_healthHandle = state.GetComponentTypeHandle<CompHealth>(true);
			_dataHandle   = state.GetComponentTypeHandle<CompData>(true);
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_entityHandle.Update(ref state);
			_unitHandle.Update(ref state);
			_healthHandle.Update(ref state);
			_dataHandle.Update(ref state);
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var entities = chunk.GetNativeArray(_entityHandle);
				var units    = chunk.GetNativeArray(ref _unitHandle);
				var healths  = chunk.GetNativeArray(ref _healthHandle);
				var datas    = chunk.GetNativeArray(ref _dataHandle);
				for (var n = 0; n < entities.Length; n++)
				{
					var health = healths[n].V;
					if (health.Hp > 0)
						continue;

					var unit = units[n].V;
					ecb.AddComponent<TagDead>(entities[n]);
					var data = datas[n].V;
					unit.RespawnTick = data.Tick + RespawnTicks;
					units[n]         = unit;
				}
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
		private EntityQuery                       _query;
		private EntityQuery                       _singletonQuery;
		private ComponentTypeHandle<CompPosition> _positionHandle;
		private ComponentTypeHandle<CompSprite>   _spriteHandle;
		private ComponentTypeHandle<CompUnit>     _unitHandle;
		private ComponentTypeHandle<CompData>     _dataHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator)
					.WithAll<CompPosition, CompSprite, CompUnit, CompData>()
					.Build(ref state);
			_singletonQuery = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAll<FramebufferSingleton>()
																				.Build(ref state);
			_positionHandle = state.GetComponentTypeHandle<CompPosition>(true);
			_spriteHandle   = state.GetComponentTypeHandle<CompSprite>(true);
			_unitHandle     = state.GetComponentTypeHandle<CompUnit>(true);
			_dataHandle     = state.GetComponentTypeHandle<CompData>(true);
			state.RequireForUpdate<FramebufferSingleton>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_positionHandle.Update(ref state);
			_spriteHandle.Update(ref state);
			_unitHandle.Update(ref state);
			_dataHandle.Update(ref state);
			var framebuffer = _singletonQuery.GetSingleton<FramebufferSingleton>()
											 .Framebuffer;
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var positions = chunk.GetNativeArray(ref _positionHandle);
				var sprites   = chunk.GetNativeArray(ref _spriteHandle);
				var units     = chunk.GetNativeArray(ref _unitHandle);
				var datas     = chunk.GetNativeArray(ref _dataHandle);
				for (var n = 0; n < sprites.Length; n++)
				{
					var position = positions[n].V;
					var sprite   = sprites[n].V;
					var unit     = units[n].V;
					var data     = datas[n].V;
					RenderSystemForEach(
						framebuffer,
						in position,
						in sprite,
						in unit,
						in data);
				}
			}
		}
	}

	private partial struct SpawnSpriteSystem : ISystem
	{
		private EntityQuery                     _query;
		private ComponentTypeHandle<CompSprite> _spritesHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompSprite>()
																	   .WithAll<TagSpawn>()
																	   .Build(ref state);
			_spritesHandle = state.GetComponentTypeHandle<CompSprite>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_spritesHandle.Update(ref state);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var sprites = chunk.GetNativeArray(ref _spritesHandle);
				for (var n = 0; n < sprites.Length; n++)
				{
					var s = sprites[n].V;
					s.Character = SpriteMask.Spawn;
					sprites[n]  = s;
				}
			}
		}
	}

	private partial struct DeadSpriteSystem : ISystem
	{
		private EntityQuery                     _query;
		private ComponentTypeHandle<CompSprite> _spritesHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompSprite>()
																	   .WithAll<TagDead>()
																	   .Build(ref state);
			_spritesHandle = state.GetComponentTypeHandle<CompSprite>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_spritesHandle.Update(ref state);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var sprites = chunk.GetNativeArray(ref _spritesHandle);
				for (var n = 0; n < sprites.Length; n++)
				{
					var s = sprites[n].V;
					s.Character = SpriteMask.Grave;
					sprites[n]  = s;
				}
			}
		}
	}

	private partial struct UnitsSpriteSystem : ISystem
	{
		private EntityQuery                     _npcQuery;
		private EntityQuery                     _heroQuery;
		private EntityQuery                     _monsterQuery;
		private ComponentTypeHandle<CompSprite> _spritesHandle;

		public void OnCreate(ref SystemState state)
		{
			_npcQuery = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompSprite>()
																		  .WithAll<TagNPC>()
																		  .WithNone<TagSpawn, TagDead>()
																		  .Build(ref state);
			_heroQuery = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompSprite>()
																		   .WithAll<TagHero>()
																		   .WithNone<TagSpawn, TagDead>()
																		   .Build(ref state);
			_monsterQuery = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAllRW<CompSprite>()
																			  .WithAll<TagMonster>()
																			  .WithNone<TagSpawn, TagDead>()
																			  .Build(ref state);
			_spritesHandle = state.GetComponentTypeHandle<CompSprite>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_spritesHandle.Update(ref state);
			ForEachSprite(ref state, ref _npcQuery,     SpriteMask.NPC);
			ForEachSprite(ref state, ref _heroQuery,    SpriteMask.Hero);
			ForEachSprite(ref state, ref _monsterQuery, SpriteMask.Monster);
		}

		private void ForEachSprite(ref SystemState state, ref EntityQuery query, SpriteMask sprite)
		{
			foreach (var chunk in query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var sprites = chunk.GetNativeArray(ref _spritesHandle);
				for (var n = 0; n < sprites.Length; n++)
				{
					var s = sprites[n].V;
					s.Character = sprite;
					sprites[n]  = s;
				}
			}
		}
	}

	private partial struct RespawnSystem : ISystem
	{
		private EntityQuery                   _query;
		private Entity                        _spawnPrefab;
		private EntityTypeHandle              _entityHandle;
		private ComponentTypeHandle<CompUnit> _unitHandle;
		private ComponentTypeHandle<CompData> _dataHandle;

		public void OnCreate(ref SystemState state)
		{
			_query = new EntityQueryBuilder(state.WorldUpdateAllocator).WithAll<CompUnit, CompData, TagDead>()
																	   .Build(ref state);
			_spawnPrefab = state.EntityManager.CreateEntity(
				typeof(CompUnit),
				typeof(CompData),
				typeof(TagSpawn),
				typeof(Prefab));
			_entityHandle = state.GetEntityTypeHandle();
			_unitHandle   = state.GetComponentTypeHandle<CompUnit>();
			_dataHandle   = state.GetComponentTypeHandle<CompData>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			_entityHandle.Update(ref state);
			_unitHandle.Update(ref state);
			_dataHandle.Update(ref state);
			var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);
			foreach (var chunk in _query.ToArchetypeChunkArray(state.WorldUpdateAllocator))
			{
				var entities = chunk.GetNativeArray(_entityHandle);
				var units    = chunk.GetNativeArray(ref _unitHandle);
				var datas    = chunk.GetNativeArray(ref _dataHandle);
				for (var n = 0; n < entities.Length; n++)
				{
					var unit = units[n].V;
					var data = datas[n].V;
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

					ecb.DestroyEntity(entities[n]);
				}
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
