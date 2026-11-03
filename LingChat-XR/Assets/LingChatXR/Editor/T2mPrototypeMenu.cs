using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using LingChatXR.Chat;

namespace LingChatXR.Editor
{
    // 仅供 PC Play Mode 原型；模型进程与候选文件留在本机。
    public static class T2mPrototypeMenu
    {
        [MenuItem("YuChat/动作实验/生成右手挥手")]
        static async void Wave()
        {
            var live=UnityEngine.Object.FindAnyObjectByType<FeiyuPreview>();
            if(!EditorApplication.isPlaying||live==null||!live.Ready||
                live.Interaction.State!=Core.PetState.Idle)
            {
                UnityEngine.Debug.LogWarning("请在 Play Mode 放置角色并等待落稳。");
                return;
            }
            var root=Path.GetFullPath(Path.Combine(Application.dataPath,"../.."));
            var output=Path.Combine(root,".cache","my-neuro-prototype","manual-"+Guid.NewGuid().ToString("N"));
            try
            {
                var player=live.Avatar.Motions;
                bool accepted=await player.TryGeneratedPrototype(Generate(root,output));
                UnityEngine.Debug.Log(accepted?"生成动作已进入当前角色":"保持当前动作："+(player.PackageLastError??"角色状态或请求已改变"));
            }
            catch(Exception error){UnityEngine.Debug.LogWarning(error.Message);}
        }

        internal static async Task<string> Generate(string root,string output,string prompt="A standing person raises the right hand and waves gently.",int seed=42)
        {
            string python=Path.Combine(root,".cache/video-motion/worker311/Scripts/python.exe");
            string tools=Path.Combine(root,"tools/video-motion");
            string port=Environment.GetEnvironmentVariable("XR_T2M_PORT")??"17880";
            if(!int.TryParse(port,out int n)||n<1||n>65535)throw new InvalidDataException("XR_T2M_PORT 无效");
            await Run(python,root,Quote(Path.Combine(tools,"capture_t2m.py"))+" "+Quote("b64:"+Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(prompt)))+" "+Quote(output)+" --port "+n+" --seed "+seed);
            string package=Path.Combine(output,"package");
            await Run(python,root,Quote(Path.Combine(tools,"convert_t2m.py"))+" "+Quote(Path.Combine(output,"motion.json"))+" "+Quote(package));
            return package;
        }
        static string Quote(string value)=>"\""+value+"\"";
        static Task Run(string executable,string root,string args)=>Task.Run(()=>
        {
            using(var process=Process.Start(new ProcessStartInfo(executable,args){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true}))
            {
                if(!process.WaitForExit(150000)){process.Kill();throw new TimeoutException("动作实验超时");}
                if(process.ExitCode!=0)throw new InvalidOperationException("动作服务或转换失败，请检查本次 result.json。");
            }
        });
    }
}
