using UnityEngine;
using UnityEngine.EventSystems;

namespace BattleBall.Net
{
    /// <summary>
    /// The on-screen joystick (joy_base and joy_knob cut from joystick.png): touch or click the base and drag, the knob follows inside the ring.
    /// <see cref="Value"/> is the direction in -1..1 on both axes (a small dead zone is cut off); on a computer the arrow keys / WASD give the same value
    /// when the joystick is not held.
    /// </summary>
    public sealed class BbJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        [Tooltip("How far the knob moves from the middle, in canvas pixels")]
        public float radius = 62f;
        public float deadZone = 0.2f;

        Vector2 _touch;
        bool _held;

        public bool Held { get { return _held; } }

        /// <summary>The direction the player pushes: the stick while held, otherwise the keyboard axes.</summary>
        public Vector2 Value
        {
            get
            {
                if (_held) return _touch;
                var typing = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (typing != null && typing.GetComponent<UnityEngine.UI.InputField>() != null) return Vector2.zero;     // letters typed in a field do not move the character
                Vector2 k = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                return k.sqrMagnitude > 1f ? k.normalized : k;
            }
        }

        public void OnPointerDown(PointerEventData e) { Move(e); _held = true; }
        public void OnDrag(PointerEventData e) { Move(e); }

        public void OnPointerUp(PointerEventData e)
        {
            _held = false;
            _touch = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        void Move(PointerEventData e)
        {
            var rect = (RectTransform)transform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, e.position, e.pressEventCamera, out local)) return;
            Vector2 centre = rect.rect.center;
            Vector2 offset = local - centre;
            Vector2 v = Vector2.ClampMagnitude(offset / radius, 1f);
            if (knob != null) knob.anchoredPosition = v * radius;
            _touch = v.magnitude < deadZone ? Vector2.zero : v;
        }

        void OnDisable()
        {
            _held = false;
            _touch = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }
    }
}
