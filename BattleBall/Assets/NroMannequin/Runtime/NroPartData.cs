using System;
using UnityEngine;

namespace NroRig
{
    /// <summary>Tuong ung PartImage (Part.cs / PartImage.cs): 1 sprite + do lech bu tru cuc bo (dx, dy).</summary>
    [Serializable]
    public struct NroPartFrame
    {
        public Sprite sprite;
        /// <summary>PartImage.dx (pixel goc, + = sang phai).</summary>
        public int dx;
        /// <summary>PartImage.dy (pixel goc, + = xuong duoi, giong client NRO).</summary>
        public int dy;
    }

    /// <summary>
    /// Tuong ung 1 Part cua NRO (Head / Body / Leg / Aura).
    /// Mang frames chinh la Part.pi[] - duoc chon theo CharInfo[cf][slot].sub.
    /// Dau: 3 frame, Than: 17 frame, Chan: 14 frame, Bag: 2 frame.
    /// </summary>
    [CreateAssetMenu(menuName = "NRO/Part Data", fileName = "NroPart_New")]
    public class NroPartData : ScriptableObject
    {
        public NroSlot slot = NroSlot.Head;
        [Tooltip("Part ID goc trong update_data/part (tuy chon, de doi chieu).")]
        public int partId;
        [Tooltip("Part.pi[] - chon theo sub-index cua tu the (CharInfo[cf][slot][0]).")]
        public NroPartFrame[] frames = new NroPartFrame[0];

        public bool TryGetFrame(int subIndex, out NroPartFrame frame)
        {
            if (frames != null && subIndex >= 0 && subIndex < frames.Length)
            {
                frame = frames[subIndex];
                return true;
            }
            frame = default(NroPartFrame);
            return false;
        }
    }
}
