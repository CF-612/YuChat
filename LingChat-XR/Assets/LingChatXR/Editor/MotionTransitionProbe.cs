using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LingChatXR.Chat;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LingChatXR.Editor
{
    public static class MotionTransitionProbe
    {
        static IEnumerator work;static double deadline;static readonly List<string> checks=new List<string>();
        static string Output=>Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
        public static void Run()
        {
            EditorSceneManager.OpenScene(PreviewProject.ScenePath);EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            deadline=EditorApplication.timeSinceStartup+240;EditorApplication.update+=Tick;EditorApplication.EnterPlaymode();
        }
        static void Tick(){try{if(EditorApplication.timeSinceStartup>deadline)throw new Exception("timeout");if(!EditorApplication.isPlaying)return;work??=Checks();if(!work.MoveNext())Finish(0);}catch(Exception e){File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());Finish(1);}}
        static IEnumerator Checks()
        {
            var reference=JObject.Parse(File.ReadAllText(Path.GetFullPath(Application.dataPath+"/../../evidence/motion-transition-2026-10-31/reference.json")));
            foreach(var f in reference["fixtures"])
            {
                string kind=(string)f["kind"];float a=(float)f["a"],actual;
                if(kind=="weight")actual=XrMotionTransition.Weight(a);
                else if(kind=="clock")actual=XrMotionTransition.EntryTime(a,(float)f["b"]);
                else actual=XrMotionTransition.PoseDuration(new[]{Quaternion.identity},new[]{Quaternion.Euler(a,0,0)},new[]{MotionRegion.Torso},MotionRegion.All,(float)f["b"],(float)f["c"]);
                Check(Mathf.Abs(actual-(float)f["expected"])<.0002f,"LingChat-reference-"+kind+"-"+a+"-"+f["b"]+"-"+f["c"]);
            }
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();live.ManualInput=true;while(!live.Ready)yield return null;
            var timings=new JArray();
            foreach(string folder in new[]{"local-smallbluefish-alpha2","local-physics-reference"})
            {
                if(live.ActivePackageFolder!=folder){var change=live.SwitchCharacterAsync(folder);while(!change.IsCompleted)yield return null;Check(change.Result,"load-"+folder);}
                live.Place(new Vector3(0,.74f,0));var avatar=live.Avatar;var player=avatar.Motions;avatar.FreezePresentation=true;while(!player.CatalogReady.IsCompleted)yield return null;
                var prefs=new XrMotionPreferences{enabled=true,conversation=false};foreach(var a in player.Catalog)prefs.items[a.id]=true;player.ApplyPreferences(prefs);player.SetSpatialAllowed(true);player.Stop(true);player.Step(0);
                var bones=avatar.Instance.Runtime.ControlRig.Bones.Values.Select(b=>b.ControlBone).ToArray();
                var basePose=bones.Select(b=>b.localRotation).ToArray();
                foreach(string id in new[]{"wave_normal_right_661","bow_normal_both_571"})
                {
                    var play=player.Play(id);while(!play.IsCompleted)yield return null;Check(play.Result,"play-"+id+folder);
                    player.Step(1f/60);Check(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,basePose[i])).Max()<.1f,"first-frame-keeps-current-pose-"+id+folder);
                    var t=player.InspectPlayback();float end=t.exitAt+t.exitDuration;bool overlap=false;
                    while(player.Busy)
                    {
                        player.Step(1f/60);t=player.InspectPlayback();
                        if(t.returnWeight>.01f&&t.returnWeight<.99f){overlap=true;Check(t.sourceTime<end-t.enterDuration/2+.001f,"return-overlaps-source-tail-"+id+folder);}
                    }
                    Check(overlap,"has-overlapped-recovery-"+id+folder);
                    Check(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,basePose[i])).Max()<.1f,"finishes-at-current-character-idle-"+id+folder);
                    timings.Add(new JObject{["model"]=folder,["id"]=id,["enterDuration"]=t.enterDuration,["returnStartsAt"]=t.exitAt,["returnDuration"]=t.exitDuration,["end"]=end});
                }
                var idle=player.SetIdle("idle_normal_both_550");while(!idle.IsCompleted)yield return null;
                player.Step(1);var wave=player.Play("wave_normal_right_661");while(!wave.IsCompleted)yield return null;
                player.Step(0);player.Step(1);player.Stop();for(int i=0;i<60;i++)player.Step(1f/60);
                Check(!player.Busy&&player.IdleId=="idle_normal_both_550","cancel-keeps-current-looping-idle-"+folder);
                var before=bones.Select(b=>b.localRotation).ToArray();player.Step(.2f);Check(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,before[i])).Max()>.01f,"idle-clock-continues-after-return-"+folder);
                player.Stop(true);avatar.FreezePresentation=false;
                var walking=live.Locomotion.GoTo(new Vector3(.12f,.74f,.08f));while(!walking.IsCompleted)yield return null;
                Check(walking.Result,"walking-starts-after-manual-actions-"+folder);
                player.ApplyPreferences(prefs);live.Locomotion.Step(.02f);
                Check(!live.Locomotion.Active&&!player.LocomotionActive,"changing-motion-preferences-cancels-walking-owner-"+folder);
            }
            File.WriteAllText(Path.Combine(Output,"timing.json"),timings.ToString());
        }
        static void Finish(int code){EditorApplication.update-=Tick;File.WriteAllText(Path.Combine(Output,"checks.json"),new JObject{["passed"]=code==0,["checks"]=JArray.FromObject(checks),["visualAccepted"]=false}.ToString());Debug.Log(code==0?"XR_M1_PASSED":"XR_TRANSITION_FAILED");EditorApplication.Exit(code);}
    }
}
