using Unity.Collections;
using UnityEngine.Jobs;
using UniGLTF.SpringBoneJobs.Blittables;
#if ENABLE_SPRINGBONE_BURST
using Unity.Burst;
#endif

namespace UniGLTF.SpringBoneJobs
{
#if ENABLE_SPRINGBONE_BURST
    [BurstCompile]
#endif
    public struct PushTransformJob : IJobParallelForTransform
    {
        [ReadOnly]
        public NativeArray<BlittableTransform> Transforms;

        public void Execute(int index, TransformAccess transform)
        {
            // 父子节点可能在同一批任务内先后写回。直接使用求解器已算好的局部旋转，避免父骨再转一次。
            transform.localRotation = Transforms[index].localRotation;
        }
    }
}