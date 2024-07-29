using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Benchmark.Entities
{

public struct Target
{
	public uint         Id;
	public TargetEntity TargetEntity;
}

public struct TargetComparer : IComparer<Target>
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int Compare(Target x, Target y) =>
		x.Id.CompareTo(y.Id);
}

}
