#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace LingChatXR.Editor
{
    // 使用 Android/Unity 标准运行时权限。首次主动按住说话时才申请麦克风。
    public sealed class QuestMicrophoneManifest:IPostGenerateGradleAndroidProject
    {
        public int callbackOrder=>1000;
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            const string android="http://schemas.android.com/apk/res/android";
            string manifest=Path.Combine(path,"src/main/AndroidManifest.xml");var xml=new XmlDocument();xml.Load(manifest);
            var app=xml.SelectSingleNode("/manifest/application");
            XmlElement metadata=null;foreach(XmlElement node in app.SelectNodes("meta-data"))if(node.GetAttribute("name",android)=="unityplayer.SkipPermissionsDialog")metadata=node;
            if(metadata==null){metadata=xml.CreateElement("meta-data");metadata.SetAttribute("name",android,"unityplayer.SkipPermissionsDialog");app.AppendChild(metadata);}
            metadata.SetAttribute("value",android,"true");
            var keyboard=xml.CreateElement("uses-feature");
            keyboard.SetAttribute("name",android,"oculus.software.overlay_keyboard");
            keyboard.SetAttribute("required",android,"false");
            bool found=false;
            foreach(XmlElement feature in xml.DocumentElement.SelectNodes("uses-feature"))
                found|=feature.GetAttribute("name",android)=="oculus.software.overlay_keyboard";
            if(!found)xml.DocumentElement.InsertBefore(keyboard,app);
            xml.Save(manifest);
        }
    }
}
#endif
