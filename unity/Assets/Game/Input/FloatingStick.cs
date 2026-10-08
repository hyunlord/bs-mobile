using SowSiege.Core;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Game.Input
{
    public sealed class FloatingStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int pointer = int.MinValue;
        private Vector2 origin, offset;
        public bool Blocked { get; set; }
        public System.Func<Vector2,bool> ExtraBlocker { get; set; }
        public bool Active => pointer != int.MinValue;
        public Vector2 Origin => origin;
        public Vector2 Offset => offset;
        public float RadiusCanvasUnits { get; set; } = 48;
        public float Radius => RadiusCanvasUnits * GetComponentInParent<Canvas>().scaleFactor;
        public PlayerInput Sample => Active && !Blocked ? new PlayerInput((short)Mathf.RoundToInt(offset.x / Radius * PlayerInput.Scale), (short)Mathf.RoundToInt(offset.y / Radius * PlayerInput.Scale)) : default;
        public void OnPointerDown(PointerEventData e) { if (Blocked || Active || ExtraBlocker?.Invoke(e.position)==true) return; pointer = e.pointerId; origin = e.position; offset = Vector2.zero; }
        public void OnDrag(PointerEventData e) { if (e.pointerId == pointer && !Blocked) offset = Vector2.ClampMagnitude(e.position - origin, Radius); }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) ResetStick(); }
        public void ResetStick() { pointer = int.MinValue; offset = Vector2.zero; }
        private void OnDisable() => ResetStick();
        private void OnApplicationFocus(bool focus) { if (!focus) ResetStick(); }
    }
}
