using UnityEngine;

namespace NroRig
{
    /// <summary>
    /// Mannequin duy nhat (khung xuong 33 tu the) cho moi nhom hinh the (thuong, hop the, Broly, khi, chibi...).
    /// Tai hien Char.paintCharBody (Char.cs:5604-5695):
    ///   X = cx + (CharInfo[cf][slot].dx + Part.dx) * cdir        (huong phai)
    ///   Y = cy - CharInfo[cf][slot].dy + Part.dy                 (man hinh, Y xuong)
    /// Z-order: Aura(0) -> Head(2) -> Leg(3) -> Body(4).
    /// Goc (0,0) cua GameObject = diem cham dat giua 2 chan (cx, cy).
    /// </summary>
    [DisallowMultipleComponent]
    public class NroMannequin : MonoBehaviour
    {
        [Header("Slot renderers (tu tao boi Prefab builder)")]
        public SpriteRenderer headRenderer;
        public SpriteRenderer legRenderer;
        public SpriteRenderer bodyRenderer;
        public SpriteRenderer auraRenderer;

        [Header("Parts (Part ID cua cai trang)")]
        public NroPartData head;
        public NroPartData leg;
        public NroPartData body;
        [Tooltip("Dau da frame (Char.Arr_Head_2Fr, vd 542<->543): chua head -> luan phien. De trong neu khong dung.")]
        public NroPartData[] headCycle;
        public NroPartData aura;

        [Header("Render")]
        [Tooltip("Pixels Per Unit cua sprite. Mac dinh 1 unit = 1 pixel goc NRO chia PPU.")]
        public float pixelsPerUnit = 100f;
        [Tooltip("He so zoom (Char.zoom). 1 = kich thuoc goc.")]
        public float zoom = 1f;
        public int sortingOrderBase = 0;
        public string sortingLayerName = "Default";

        [Header("Trang thai")]
        public NroPose pose = NroPose.Stand_1;
        [Tooltip("+1 = quay phai, -1 = quay trai (lat guong).")]
        public int facing = 1;
        [Tooltip("Cung Char.isMonkey - ep ch = 60.")]
        public bool isMonkey;
        [Tooltip("Cung Char.isFusion - ep ch = 60.")]
        public bool isFusion;

        [Header("Dau da frame")]
        [Tooltip("So game tick / giay dung de doi frame dau (client NRO: doi moi 4 tick).")]
        public float ticksPerSecond = 25f;
        public int ticksPerHeadFrame = 4;

        private float _headTimer;
        private int _headTick;

        // ------- Gia tri tinh toan theo cong thuc client (don vi pixel goc) -------

        /// <summary>Char.ch (Char.cs:5679). Chieu cao va cham / mep tren.</summary>
        public int CharHeight
        {
            get
            {
                if (isMonkey || isFusion) return 60;
                int dy0 = 0;
                NroPartFrame f;
                if (head != null && head.TryGetFrame(NroPoseTable.Sub(0, NroSlot.Head), out f)) dy0 = f.dy;
                return NroPoseTable.Dy(0, NroSlot.Head) + dy0 + 10;
            }
        }

        /// <summary>
        /// Khoang cach tu mat dat toi thanh HP/ten (Char.cs:5266-5278), don vi pixel goc, huong LEN.
        /// num = CharInfo[cf][0].dy - head.pi.dy + 5 (+5 neu la nhan vat nguoi choi).
        /// </summary>
        public float GetOverheadOffsetPixels(bool isMe)
        {
            int dyHead = 0;
            NroPartFrame f;
            if (head != null && head.TryGetFrame(NroPoseTable.Sub((int)pose, NroSlot.Head), out f)) dyHead = f.dy;
            int num = NroPoseTable.Dy((int)pose, NroSlot.Head) - dyHead + 5;
            if (isMe) num += 5;
            return num;
        }

        /// <summary>Vi tri the gioi (Unity units) de dat HP bar / ten phia tren dau.</summary>
        public Vector3 GetOverheadWorldPosition(bool isMe)
        {
            return transform.position + Vector3.up * (GetOverheadOffsetPixels(isMe) * zoom / pixelsPerUnit);
        }

        // ------- API -------

        public void SetPose(NroPose newPose)
        {
            pose = newPose;
            Apply();
        }

        public void SetFacing(int dir)
        {
            facing = dir >= 0 ? 1 : -1;
            Apply();
        }

        /// <summary>Mac do: truyen 3 Part (nhu 3 con so head/body/leg cua cai trang).</summary>
        public void SetParts(NroPartData newHead, NroPartData newBody, NroPartData newLeg)
        {
            head = newHead;
            body = newBody;
            leg = newLeg;
            headCycle = null;
            Apply();
        }

        public void SetHeadCycle(NroPartData[] cycle)
        {
            headCycle = cycle;
            if (cycle != null && cycle.Length > 0) head = cycle[0];
            Apply();
        }

        private void Update()
        {
            if (headCycle == null || headCycle.Length < 2) return;
            _headTimer += Time.deltaTime * ticksPerSecond;
            int tick = (int)_headTimer;
            if (tick == _headTick) return;
            _headTick = tick;
            Apply();
        }

        private void LateUpdate()
        {
            // Giu bo cuc dung neu Inspector thay doi trong khi chay.
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) Apply();
        }
#endif

        public void Apply()
        {
            int cf = Mathf.Clamp((int)pose, 0, NroPoseTable.PoseCount - 1);

            NroPartData headNow = head;
            if (headCycle != null && headCycle.Length > 0)
            {
                int idx = (_headTick / Mathf.Max(1, ticksPerHeadFrame)) % headCycle.Length;
                headNow = headCycle[idx] != null ? headCycle[idx] : head;
            }

            PlaceSlot(headRenderer, headNow, NroSlot.Head, cf, 2);
            PlaceSlot(legRenderer, leg, NroSlot.Leg, cf, 3);
            PlaceSlot(bodyRenderer, body, NroSlot.Body, cf, 4);
            PlaceSlot(auraRenderer, aura, NroSlot.Aura, cf, 0);
        }

        private void PlaceSlot(SpriteRenderer r, NroPartData part, NroSlot slot, int cf, int layer)
        {
            if (r == null) return;

            NroPartFrame f;
            if (part == null || !part.TryGetFrame(NroPoseTable.Sub(cf, slot), out f) || f.sprite == null)
            {
                r.enabled = false;
                return;
            }

            r.enabled = true;
            r.sprite = f.sprite;
            r.flipX = facing < 0;
            r.sortingLayerName = sortingLayerName;
            r.sortingOrder = sortingOrderBase + layer;

            // Toa do client (pixel, Y huong xuong) -> Unity (Y huong len), mep tren-trai (hoac tren-phai khi lat).
            float px = (NroPoseTable.Dx(cf, slot) + f.dx) * facing;
            float py = NroPoseTable.Dy(cf, slot) - f.dy;
            float k = zoom / pixelsPerUnit;

            Rect rect = f.sprite.rect;
            float spritePpu = f.sprite.pixelsPerUnit > 0f ? f.sprite.pixelsPerUnit : pixelsPerUnit;
            float h = rect.height / spritePpu * zoom;
            Vector2 pivot = f.sprite.pivot / spritePpu * zoom; // tu goc duoi-trai

            // Dat sao cho goc TOP-LEFT (huong phai) hoac TOP-RIGHT (lat) cua sprite nam tai (X, Y) - giong drawSmallImage anchor.
            float originX = px * k + (facing > 0 ? pivot.x : -pivot.x);
            float originY = py * k - (h - pivot.y);

            r.transform.localPosition = new Vector3(originX, originY, 0f);
            r.transform.localScale = new Vector3(zoom, zoom, 1f);
        }
    }
}
