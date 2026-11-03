using UnityEngine;
namespace LingChatXR
{
    // 桌面高度提示；不是环境遮挡或真实 MR 阴影。
    public sealed class ContactShadow : MonoBehaviour
    {
        private Transform follow;
        private float plane;
        private float radius;
        private Texture2D texture;
        private Material material;
        private GameObject quad;
        public void SetRadius(float value)=>radius=value;
        public void SetPlane(float value)=>plane=value;
        public void Configure(Transform target,float tableY,float footRadius)
        {
            follow=target;plane=tableY;radius=footRadius;
            texture=new Texture2D(64,64,TextureFormat.RGBA32,false);texture.wrapMode=TextureWrapMode.Clamp;
            for(int y=0;y<64;y++)for(int x=0;x<64;x++)
            {
                float d=new Vector2((x-31.5f)/31.5f,(y-31.5f)/31.5f).magnitude;
                texture.SetPixel(x,y,new Color(.02f,.04f,.065f,Mathf.Pow(Mathf.Clamp01(1-d),2)*.4f));
            }
            texture.Apply();material=new Material(Shader.Find("Sprites/Default")){mainTexture=texture};
            quad=GameObject.CreatePrimitive(PrimitiveType.Quad);quad.name="Contact height shadow";quad.transform.SetParent(transform,false);
            Destroy(quad.GetComponent<Collider>());quad.GetComponent<MeshRenderer>().sharedMaterial=material;
            quad.transform.rotation=Quaternion.Euler(90,0,0);
        }
        private void LateUpdate()
        {
            if(quad==null||follow==null)return;
            quad.SetActive(follow.gameObject.activeInHierarchy);float height=Mathf.Max(0,follow.position.y-plane);
            quad.transform.position=new Vector3(follow.position.x,plane+.0015f,follow.position.z);
            float size=radius*3f+Mathf.Min(height,.65f)*.13f;quad.transform.localScale=new Vector3(size,size,1);
            material.color=new Color(1,1,1,1/(1+height*8));
        }
        private void OnDestroy(){if(quad!=null)Destroy(quad);if(material!=null)Destroy(material);if(texture!=null)Destroy(texture);}
    }
}
