using System;
using UnityEngine;
using UnityEngine.UI;
namespace Game.View
{
    public sealed class FoundationStatus : MonoBehaviour
    {
        public string Message { get; set; } = "준비 중";
        public bool Failed { get; set; }
        public static Color Ground => GamePalette.Ground;
        private UiShell ui;
        private Text label;
        private Font font;
        private void Start()
        {
            try
            {
                font=FontProvider.Create(Array.Empty<string>());
                var root=new GameObject("Boot status",typeof(RectTransform));root.transform.SetParent(transform,false);ui=root.AddComponent<UiShell>();ui.Initialize(font);
                label=ui.Label(ui.Panel("Boot"),Message,GamePalette.BodySize,240);
            }
            catch(Exception e){Message="Unable to display Korean text: "+e.Message;UnityEngine.Debug.LogError(e.Message);}
        }
        private void Update(){if(label!=null){label.text=Message;label.color=Failed?GamePalette.Danger:GamePalette.Text;}}
        private void OnGUI(){if(label==null)GUI.Label(new Rect(16,24,Screen.width-32,Screen.height-48),Message);}
        private void OnDestroy(){if(ui!=null)Destroy(ui.gameObject);if(font!=null)Destroy(font);}
    }
}
