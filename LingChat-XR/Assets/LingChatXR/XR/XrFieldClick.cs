using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace LingChatXR.XR
{
    public sealed class XrFieldClick : MonoBehaviour, IPointerClickHandler
    {
        public Action Click;
        public void OnPointerClick(PointerEventData eventData) => Click?.Invoke();
    }
}
