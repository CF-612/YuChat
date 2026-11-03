using System;
using System.Reflection;
using LingChatXR.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Gestures;
using UnityEngine.XR.Hands.ProviderImplementation;

namespace LingChatXR.Editor
{
    // 以官方 Joint/Hand 数据构造输入，仍经过 XRHandShape。不是识别结果布尔值的替身。
    static class WalkHandFixture
    {
        public static void Feed(QuestEntry entry,Vector3 palmWorld,Quaternion rotation,bool point,bool tracked=true)
        {
            var hand=XRHandProviderUtility.CreateHand(Handedness.Right,Allocator.Temp);
            try
            {
                object boxed=hand;typeof(XRHand).GetProperty("isTracked").GetSetMethod(true).Invoke(boxed,new object[]{tracked});hand=(XRHand)boxed;
                var joints=(NativeArray<XRHandJoint>)typeof(XRHand).GetField("m_Joints",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(hand);
                var localPalm=entry.TrackingSpace.InverseTransformPoint(palmWorld);var localRotation=Quaternion.Inverse(entry.TrackingSpace.rotation)*rotation;
                void Set(XRHandJointID id,Vector3 p,Quaternion q){joints[id.ToIndex()]=XRHandProviderUtility.CreateJoint(Handedness.Right,XRHandJointTrackingState.Pose,id,new Pose(localPalm+localRotation*p,localRotation*q));}
                Set(XRHandJointID.Palm,Vector3.zero,Quaternion.identity);Set(XRHandJointID.Wrist,new Vector3(0,0,-.03f),Quaternion.identity);
                foreach(var finger in new[]{XRHandFingerID.Index,XRHandFingerID.Middle,XRHandFingerID.Ring,XRHandFingerID.Little})
                {
                    bool curled=point&&finger!=XRHandFingerID.Index;Vector3 p=new Vector3(((int)finger-1)*.02f,0,.015f);Quaternion q=Quaternion.identity;
                    var start=finger.GetFrontJointID();
                    for(int i=0;i<5;i++){Set(start+i,p,q);if(curled&&i>=1)q*=Quaternion.Euler(65,0,0);p+=q*Vector3.forward*.022f;}
                }
                // 没有原生 subsystem 的夹具必须执行与官方 FingerStateProcessor 相同的缓存失效。
                var cache=(Array)typeof(XRFingerShapeMath).GetField("k_CachedFingerShapes",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);Array.Clear(cache,0,cache.Length);
                entry.WalkGestures.ReadHand(new XRHandJointsUpdatedEventArgs{hand=hand},false);
            }
            finally{XRHandProviderUtility.DisposeHand(hand);}
        }
    }
}
