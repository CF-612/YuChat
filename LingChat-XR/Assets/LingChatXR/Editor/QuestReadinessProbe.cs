using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.XR.OpenXR;
using UnityEngine;
using UnityEngine.XR.OpenXR.Features;

namespace LingChatXR.Editor
{
    // 只报告官方规则，不调用 Fix All，不切换用户运行时或构建平台。
    public static class QuestReadinessProbe
    {
        [Serializable] sealed class Issue { public string target,message,fix;public bool error,blocksPlay; }
        [Serializable] sealed class Result { public bool inspectionCompleted;public bool deviceTested;public List<Issue> issues=new List<Issue>(); }
        public static void Run()
        {
            var result=new Result();
            // Meta Android 规则依赖当前活动平台；PC 首测只检查 Standalone。
            foreach(var target in new[]{BuildTargetGroup.Standalone})
            {
                var issues=new List<OpenXRFeature.ValidationRule>();
                OpenXRProjectValidation.GetCurrentValidationIssues(issues,target);
                foreach(var issue in issues)result.issues.Add(new Issue{target=target.ToString(),message=issue.message,fix=issue.fixItMessage,error=issue.error,blocksPlay=issue.errorEnteringPlaymode});
            }
            result.inspectionCompleted=true;
            string directory=Environment.GetEnvironmentVariable("XR_M1_EVIDENCE");
            File.WriteAllText(Path.Combine(directory,"official-validation.json"),JsonUtility.ToJson(result,true));
            // 标记检查执行完成，是否可试玩/构建必须读取报告中的 error 字段。
            Debug.Log("XR_READINESS_ISSUES: "+result.issues.Count+"; errors="+result.issues.Count(x=>x.error));
            Debug.Log("XR_M1_PASSED");EditorApplication.Exit(0);
        }
    }
}
