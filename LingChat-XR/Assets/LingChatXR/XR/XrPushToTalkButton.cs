using LingChatXR.Chat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LingChatXR.XR
{
    // 原 uGUI / XRI 指针事件；移出按钮、失追和页面关闭都丢弃录音。
    public sealed class XrPushToTalkButton:MonoBehaviour,IPointerDownHandler,IPointerUpHandler,IPointerExitHandler
    {
        public XrPushToTalk VoiceInput;
        int? pointer;
        public void OnPointerDown(PointerEventData e){if(pointer==null&&GetComponent<Button>().IsInteractable()&&VoiceInput.Begin())pointer=e.pointerId;}
        public void OnPointerUp(PointerEventData e){if(pointer!=e.pointerId)return;pointer=null;VoiceInput.Release();}
        public void OnPointerExit(PointerEventData e){if(pointer!=e.pointerId)return;pointer=null;VoiceInput.Cancel();}
        void OnDisable(){pointer=null;if(VoiceInput!=null)VoiceInput.Cancel();}
    }
}
